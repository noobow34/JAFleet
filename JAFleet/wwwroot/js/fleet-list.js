/*
 * 機体一覧（航空会社・型式ごとの一覧と検索結果で共用）
 *
 * 1 機ぶんの DOM は 1 つで、並べ方だけを CSS（fleet-list.css）で幅に合わせて組み替える。
 *   スマホ（〜599px）   : 1 機 2 行のカード風。1 行目にレジ・機種・運用、2 行目にそれ以外
 *   タブレット（600〜991px）: 表。機体記号と製造番号、機種と航空会社、特別塗装と備考を上下に重ねる
 *   PC（992px〜）       : 表。1 機 1 行で全項目を横に並べる
 * DOM の並び順はおおむねスマホの並び順で（Wi-Fi だけ CSS の order で 1 行目へ上げる）、表のときは grid-area で列へ割り当てる。
 *
 * 使い方:
 *   const list = FleetList.create('#id', { isAdmin: true });
 *   list.load(JfHttp.get(url));     // 読み込み中の表示 → 結果の表示。返す Promise は行の配列（失敗したときは null）で解決する
 *   list.setData(rows);             // API が返す aircraft_view の配列
 *   list.clear('メッセージ');        // 空にして案内を出す
 *   list.resetSort();               // 既定の並び（display_order）に戻す
 */
(function (window) {
    'use strict';

    // データ更新から何日以内を「最近更新」として印を付けるか
    const NEW_DAYS = 7;
    // Excel 出力は押したときだけ読み込む（一覧を開くたびに 900KB を取りに行かないため）
    const XLSX_URL = 'https://cdnjs.cloudflare.com/ajax/libs/xlsx/0.18.5/xlsx.full.min.js';

    const collator = new Intl.Collator('ja', { numeric: true, sensitivity: 'base' });

    // 運用コード（JAFleet.Commons.Constants.OperationCode）→ バッジの色
    const OP_CLASS = {
        '0': 'pre', '1': 'pre', '2': 'dlv',
        '3': 'intl', '4': 'dom', '5': 'both', '6': 'cargo',
        '7': 'retire', '8': 'retire', '9': 'other'
    };
    const RETIRE_CODES = ['7', '8'];

    const WIFI_CLASS = { '0': 'no', '1': 'yes', '2': 'part' };

    const seatTotal = function (r) {
        const seats = [r.f, r.c, r.py, r.p, r.j, r.y].filter(function (v) { return v != null; });
        return seats.length === 0 ? null : seats.reduce(function (a, b) { return a + b; }, 0);
    };

    // 並べ替えと Excel の列。first は見出しを初めて押したときの向き、width は Excel の列幅。
    // optional の列は、一覧にその値を持つ機体が 1 機でもあるときだけ出す
    const COLUMNS = [
        { key: 'air', label: '航空会社', first: 'asc', width: 14, value: function (r) { return r.airlineNameJpShort; }, xlsx: function (r) { return r.airlineNameJpShort; } },
        { key: 'type', label: '機種', first: 'asc', width: 16, value: function (r) { return r.typeDetailName; }, xlsx: function (r) { return r.typeDetailName; } },
        { key: 'reg', label: '機体記号', first: 'asc', width: 10, value: function (r) { return r.registrationNumber; }, xlsx: function (r) { return r.registrationNumber + (r.testRegistration ? '(' + r.testRegistration + ')' : ''); } },
        { key: 'sn', label: '製造番号', first: 'asc', width: 10, value: function (r) { return r.serialNumber; }, xlsx: function (r) { return r.serialNumber; } },
        { key: 'date', label: '登録年月日', first: 'desc', width: 12, ascLabel: '古い順', descLabel: '新しい順', value: function (r) { return r.registerDate; }, xlsx: function (r) { return r.registerDate; } },
        { key: 'dereg', label: '抹消年月日', first: 'desc', width: 12, optional: true, ascLabel: '古い順', descLabel: '新しい順', value: function (r) { return r.deregisterDate; }, xlsx: function (r) { return r.deregisterDate; } },
        { key: 'op', label: '運用', first: 'asc', width: 12, value: function (r) { return r.operationCode; }, xlsx: function (r) { return r.operation; } },
        { key: 'cfg', label: 'コンフィグ', first: 'desc', width: 26, ascLabel: '席数が少ない順', descLabel: '席数が多い順', value: seatTotal, xlsx: function (r) { return r.seatConfig; } },
        { key: 'wifi', label: 'Wi-Fi', first: 'desc', width: 6, value: function (r) { return r.wifiCode; }, xlsx: function (r) { return r.wifiShort; } },
        { key: 'liv', label: '特別塗装', first: 'asc', width: 30, value: function (r) { return r.specialLivery; }, xlsx: function (r) { return r.specialLivery; } },
        { key: 'rem', label: '備考', first: 'asc', width: 40, value: function (r) { return r.remarks; }, xlsx: function (r) { return r.remarks; } }
    ];

    const COLUMN_BY_KEY = {};
    COLUMNS.forEach(function (c) { COLUMN_BY_KEY[c.key] = c; });

    const esc = function (s) {
        if (s == null) { return ''; }
        return String(s).replace(/[&<>"']/g, function (ch) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch];
        });
    };

    // 値の無いものは向きに関係なく常に後ろへ
    const compareValues = function (a, b) {
        const na = a == null || a === '';
        const nb = b == null || b === '';
        if (na || nb) { return na === nb ? 0 : (na ? 1 : -1); }
        if (typeof a === 'number' && typeof b === 'number') { return a - b; }
        return collator.compare(String(a), String(b));
    };

    const isRecentlyUpdated = function (r, now) {
        if (!r.updateTime) { return false; }
        const t = Date.parse(r.updateTime);
        return !isNaN(t) && now - t <= NEW_DAYS * 86400000;
    };

    // 「E190-E2(E190-300STD)」の括弧から先は補足なので小さく出す（狭いときはそこで折り返す）
    const typeHtml = function (name) {
        if (!name) { return ''; }
        const p = name.indexOf('(');
        if (p <= 0) { return esc(name); }
        return esc(name.substring(0, p)) + '<wbr><span class="fl-type-sub">' + esc(name.substring(p)) + '</span>';
    };

    const buildRowHtml = function (r, i, opt, now) {
        const reg = esc(r.registrationNumber);
        const opClass = OP_CLASS[r.operationCode] || 'other';
        const classes = ['fl-row'];
        if (isRecentlyUpdated(r, now)) { classes.push('is-new'); }
        if (RETIRE_CODES.indexOf(r.operationCode) >= 0) { classes.push('is-retired'); }

        let h = '<div class="' + classes.join(' ') + '" role="row" data-i="' + i + '">';

        // ---- 1 行目（スマホ）: レジ・機種・運用 ----
        h += '<div class="fl-c c-reg" role="cell"><a class="fl-reg" href="/AD/' + encodeURIComponent(r.registrationNumber) + '" data-reg="' + reg + '">' + reg + '</a>';
        if (r.testRegistration) { h += '<span class="fl-test">' + esc(r.testRegistration) + '</span>'; }
        h += '</div>';
        h += '<div class="fl-c c-type" role="cell">' + typeHtml(r.typeDetailName) + '</div>';
        h += '<div class="fl-c c-op" role="cell">' + (r.operation ? '<span class="fl-op op-' + opClass + '">' + esc(r.operation) + '</span>' : '') + '</div>';
        h += '<i class="fl-br br1" aria-hidden="true"></i>';

        // ---- 2 行目（スマホ）: 航空会社・製造番号・登録年月日・コンフィグ（Wi-Fi は 1 行目の右に出す） ----
        h += '<div class="fl-c c-air" role="cell" title="' + esc(r.airlineNameJpShort) + '">' + esc(r.airlineNameJpShort) + '</div>';
        h += '<div class="fl-c c-sn" role="cell">';
        if (r.serialNumber) {
            h += '<span class="fl-lbl">製番</span>';
            h += opt.isAdmin
                ? '<a class="fl-edit" href="/E/' + encodeURIComponent(r.registrationNumber) + '" data-reg="' + reg + '" title="編集">' + esc(r.serialNumber) + '</a>'
                : esc(r.serialNumber);
        }
        h += '</div>';
        h += '<div class="fl-c c-date" role="cell">' + (r.registerDate ? '<span class="fl-lbl">登録</span>' + esc(r.registerDate) : '') + '</div>';
        // 抹消年月日の列は、一覧に抹消済みの機体が 1 機でもあるときだけ出す（.fl--has-dereg）
        h += '<div class="fl-c c-dereg" role="cell">' + (r.deregisterDate ? '<span class="fl-lbl">抹消</span>' + esc(r.deregisterDate) : '') + '</div>';
        h += '<div class="fl-c c-cfg" role="cell">';
        if (r.seatConfig) {
            h += r.seatUrl
                ? '<a href="' + esc(r.seatUrl) + '" target="_blank" rel="noopener">' + esc(r.seatConfig) + '</a>'
                : esc(r.seatConfig);
        }
        h += '</div>';
        h += '<div class="fl-c c-wifi" role="cell">';
        if (r.wifiShort) {
            // △（エンタメのみ）のような補足は PC では title、スマホでは本文に出す
            const extra = r.wifi && r.wifi !== r.wifiShort ? r.wifi.substring(r.wifiShort.length) : '';
            h += '<span class="fl-wifi w-' + (WIFI_CLASS[r.wifiCode] || 'no') + '"' + (r.wifi ? ' title="Wi-Fi ' + esc(r.wifi) + '"' : '') + '>'
                + '<span class="fl-lbl">Wi-Fi</span>' + esc(r.wifiShort)
                + (extra ? '<span class="fl-wifi-extra">' + esc(extra) + '</span>' : '')
                + '</span>';
        }
        h += '</div>';

        // ---- 3 行目（スマホ、あるときだけ）: 特別塗装・備考 ----
        if (r.specialLivery || r.remarks) { h += '<i class="fl-br br2" aria-hidden="true"></i>'; }
        h += '<div class="fl-c c-liv" role="cell">' + (r.specialLivery ? '<span class="fl-livery" title="' + esc(r.specialLivery) + '">' + esc(r.specialLivery) + '</span>' : '') + '</div>';
        h += '<div class="fl-c c-rem" role="cell">' + (r.remarks ? '<span class="fl-rem" title="' + esc(r.remarks) + '">' + esc(r.remarks) + '</span>' : '') + '</div>';

        h += '</div>';
        return h;
    };

    const buildHeadHtml = function () {
        let h = '<div class="fl-head" role="row">';
        COLUMNS.forEach(function (c) {
            // optional の列の見出しも置いておき、出すかどうかは CSS（.fl--has-dereg）で切り替える
            h += '<button type="button" class="fl-h c-' + c.key + '" role="columnheader" data-sort="' + c.key + '" aria-sort="none">' + esc(c.label) + '</button>';
        });
        return h + '</div>';
    };

    const buildSortOptions = function (visible) {
        let h = '<option value="">標準の並び</option>';
        COLUMNS.filter(visible).forEach(function (c) {
            const dirs = c.first === 'asc' ? ['asc', 'desc'] : ['desc', 'asc'];
            dirs.forEach(function (d) {
                const suffix = d === 'asc' ? (c.ascLabel || '昇順') : (c.descLabel || '降順');
                h += '<option value="' + c.key + ':' + d + '">' + esc(c.label) + '（' + suffix + '）</option>';
            });
        });
        return h;
    };

    // 固定ナビバーの高さに見出しの固定位置を合わせる（タブレット幅ではナビバーが折り返して高くなる）
    const syncNavbarOffset = function () {
        const nav = document.querySelector('.navbar-fixed-top');
        if (!nav || nav.querySelector('.navbar-collapse.in')) { return; }
        const h = nav.offsetHeight;
        document.documentElement.style.setProperty('--fl-top', h + 'px');
        const body = document.body;
        if (body.dataset.flBasePadding == null) { body.dataset.flBasePadding = parseFloat(getComputedStyle(body).paddingTop) || 0; }
        body.style.paddingTop = Math.max(parseFloat(body.dataset.flBasePadding), h + 8) + 'px';
    };

    let xlsxLoading = null;
    const loadXlsx = function () {
        if (window.XLSX) { return Promise.resolve(); }
        if (!xlsxLoading) {
            xlsxLoading = new Promise(function (resolve, reject) {
                const s = document.createElement('script');
                s.src = XLSX_URL;
                s.crossOrigin = 'anonymous';
                s.onload = function () { resolve(); };
                s.onerror = function () { xlsxLoading = null; reject(); };
                document.head.appendChild(s);
            });
        }
        return xlsxLoading;
    };

    // 表（1 行目が見出しの 2 次元配列）を Excel に書き出す。非稼働情報の画面からも使う。
    // 返す Promise は書き出しが終わったら（失敗して案内を出したときも）解決する
    const exportXlsx = function (aoa, widths, fileName) {
        return loadXlsx().then(function () {
            const rows = aoa.map(function (row) { return row.map(function (v) { return v == null ? '' : v; }); });
            const ws = window.XLSX.utils.aoa_to_sheet(rows);
            if (widths) { ws['!cols'] = widths.map(function (w) { return { wch: w }; }); }
            const wb = window.XLSX.utils.book_new();
            window.XLSX.utils.book_append_sheet(wb, ws, 'Sheet1');
            const name = (fileName || document.title).replace(/[\\/:*?"<>|]/g, '_');
            window.XLSX.writeFile(wb, name + '.xlsx');
            fetch('/Dummy?excel', { cache: 'no-store' });
        }, function () {
            JfDialog.alert('Excel出力の準備に失敗しました。通信状況を確かめて、もう一度押してください。');
        });
    };

    const create = function (selector, options) {
        const opt = Object.assign({ isAdmin: false, emptyText: '該当する機体はありません', exportName: null }, options);
        const root = typeof selector === 'string' ? document.querySelector(selector) : selector;
        root.classList.add('fl');

        root.innerHTML = (
            '<div class="fl-bar">'
            + '<div class="fl-search"><input type="search" class="fl-filter" placeholder="レジ・機種・塗装などで絞り込み" aria-label="一覧を絞り込む" autocomplete="off" /></div>'
            + '<div class="fl-tools">'
            + '<span class="fl-count" aria-live="polite"></span>'
            + '<button type="button" class="fl-btn fl-reset" hidden>並びを戻す</button>'
            + '<select class="fl-sortsel" aria-label="並べ替え">' + buildSortOptions(function (c) { return !c.optional; }) + '</select>'
            + '<span class="fl-legend"><i></i>1週間以内に更新</span>'
            + '<button type="button" class="fl-btn fl-excel">Excel出力</button>'
            + '</div>'
            + '</div>'
            + '<div class="fl-table" role="table" aria-label="機体一覧">' + buildHeadHtml() + '<div class="fl-body" role="rowgroup"></div></div>'
            + '<div class="fl-msg"></div>'
        );

        const body = root.querySelector('.fl-body');
        const head = root.querySelector('.fl-head');
        const msg = root.querySelector('.fl-msg');
        const count = root.querySelector('.fl-count');
        const filterBox = root.querySelector('.fl-filter');
        const sortSel = root.querySelector('.fl-sortsel');
        const resetBtn = root.querySelector('.fl-reset');

        const showMsg = function (text, kind) {
            msg.textContent = text;
            msg.setAttribute('data-kind', kind);
            msg.style.display = '';
        };

        let rows = [];       // API の行。_html と _text を持たせる
        let shown = [];      // いま表示している行（絞り込み・並べ替え後）
        let sort = null;     // { key, dir } / null は display_order のまま
        let filterText = '';
        let state = 'empty'; // empty / loading / ready

        // 抹消済みの機体が 1 機でもあれば抹消年月日の列を出す
        let hasDereg = false;
        const isVisible = function (c) { return !c.optional || hasDereg; };

        const applyLayoutClass = function () {
            // 1 社だけの一覧では航空会社の列を出さない（全行同じで場所を取るだけ）
            const airlines = {};
            rows.forEach(function (r) { airlines[r.airline] = true; });
            root.classList.toggle('fl--one-airline', Object.keys(airlines).length === 1);

            hasDereg = rows.some(function (r) { return !!r.deregisterDate; });
            root.classList.toggle('fl--has-dereg', hasDereg);
            if (sort && !isVisible(COLUMN_BY_KEY[sort.key])) { sort = null; }
            sortSel.innerHTML = buildSortOptions(isVisible);
        };

        const render = function () {
            let list = rows;
            if (filterText) {
                const words = filterText.toLowerCase().split(/\s+/).filter(Boolean);
                list = list.filter(function (r) {
                    return words.every(function (w) { return r._text.indexOf(w) >= 0; });
                });
            }
            if (sort) {
                const col = COLUMN_BY_KEY[sort.key];
                const sign = sort.dir === 'asc' ? 1 : -1;
                list = list.slice().sort(function (a, b) {
                    const va = col.value(a), vb = col.value(b);
                    const empty = (va == null || va === '') - (vb == null || vb === '');
                    if (empty !== 0) { return empty; }
                    return sign * compareValues(va, vb) || a._i - b._i;
                });
            }
            shown = list;
            body.innerHTML = list.map(function (r) { return r._html; }).join('');

            head.querySelectorAll('.fl-h').forEach(function (h) {
                h.classList.remove('is-asc', 'is-desc');
                h.setAttribute('aria-sort', 'none');
            });
            if (sort) {
                const h = head.querySelector('.fl-h[data-sort="' + sort.key + '"]');
                h.classList.add('is-' + sort.dir);
                h.setAttribute('aria-sort', sort.dir === 'asc' ? 'ascending' : 'descending');
            }
            sortSel.value = sort ? sort.key + ':' + sort.dir : '';
            resetBtn.hidden = !sort;

            if (state === 'loading') {
                count.textContent = '';
                showMsg('読み込み中…', 'loading');
            } else if (rows.length === 0) {
                count.textContent = '';
                showMsg(state === 'empty' ? opt.emptyText : '該当する機体はありません', 'empty');
            } else if (list.length === 0) {
                count.innerHTML = '<b>0</b> / ' + rows.length + ' 機';
                showMsg('「' + filterText + '」に当てはまる機体はありません', 'empty');
            } else {
                count.innerHTML = filterText ? '<b>' + list.length + '</b> / ' + rows.length + ' 機' : '<b>' + rows.length + '</b> 機';
                msg.style.display = 'none';
            }
            root.classList.toggle('is-blank', list.length === 0);
        };

        const api = {
            setData: function (data) {
                const now = Date.now();
                rows = (data || []).map(function (r, i) {
                    const o = Object.assign({}, r);
                    o._i = i;
                    o._html = buildRowHtml(r, i, opt, now);
                    o._text = [r.registrationNumber, r.testRegistration, r.typeDetailName, r.typeName, r.airlineNameJpShort,
                        r.serialNumber, r.registerDate, r.operation, r.seatConfig, r.specialLivery, r.remarks, r.deregisterDate]
                        .filter(function (v) { return v != null; }).join(' ').toLowerCase();
                    return o;
                });
                state = 'ready';
                applyLayoutClass();
                render();
                return api;
            },
            load: function (promise) {
                rows = [];
                state = 'loading';
                render();
                return Promise.resolve(promise).then(function (data) {
                    api.setData(data);
                    return data;
                }, function () {
                    state = 'ready';
                    render();
                    showMsg('一覧を読み込めませんでした。時間をおいて開き直してください', 'error');
                    return null;
                });
            },
            clear: function (text) {
                rows = [];
                state = 'empty';
                if (text) { opt.emptyText = text; }
                render();
                return api;
            },
            resetSort: function () {
                sort = null;
                render();
                return api;
            },
            rows: function () { return shown.slice(); }
        };

        // ---- 並べ替え ----
        head.addEventListener('click', function (e) {
            const button = e.target.closest('.fl-h');
            if (!button) { return; }
            const key = button.getAttribute('data-sort');
            const col = COLUMN_BY_KEY[key];
            const other = col.first === 'asc' ? 'desc' : 'asc';
            // 初めの向き → 逆向き → 標準の並び、の順に回る
            if (!sort || sort.key !== key) { sort = { key: key, dir: col.first }; }
            else if (sort.dir === col.first) { sort = { key: key, dir: other }; }
            else { sort = null; }
            render();
        });
        sortSel.addEventListener('change', function () {
            const v = sortSel.value;
            sort = v ? { key: v.split(':')[0], dir: v.split(':')[1] } : null;
            render();
        });
        resetBtn.addEventListener('click', function () { api.resetSort(); });

        // ---- 絞り込み ----
        let filterTimer = null;
        filterBox.addEventListener('input', function () {
            clearTimeout(filterTimer);
            filterTimer = setTimeout(function () {
                filterText = filterBox.value.trim();
                render();
            }, 120);
        });

        // ---- 詳細を開く（行のどこを押しても開く。中のリンクはそれぞれの動きを優先） ----
        const plainClick = function (e) { return !(e.ctrlKey || e.metaKey || e.shiftKey || e.altKey || e.button > 0); };
        body.addEventListener('click', function (e) {
            const regLink = e.target.closest('a.fl-reg');
            if (regLink) {
                if (!plainClick(e)) { return; }
                e.preventDefault();
                openDetail(regLink.getAttribute('data-reg'));
                return;
            }
            const editLink = e.target.closest('a.fl-edit');
            if (editLink) {
                if (!plainClick(e)) { return; }
                e.preventDefault();
                openPopUp('/E/' + encodeURIComponent(editLink.getAttribute('data-reg')) + '?nohead=true');
                return;
            }
            const row = e.target.closest('.fl-row');
            if (!row || e.target.closest('a, button, input, select')) { return; }
            // 文字を選んでいるときは開かない（レジや製造番号をコピーしたいとき）
            const sel = window.getSelection ? String(window.getSelection()) : '';
            if (sel) { return; }
            openDetail(row.querySelector('a.fl-reg').getAttribute('data-reg'));
        });

        // ---- Excel 出力（表示中の行を、表示中の並びで） ----
        const excelBtn = root.querySelector('.fl-excel');
        excelBtn.addEventListener('click', function () {
            const btn = excelBtn;
            if (shown.length === 0) { return; }
            btn.disabled = true;
            btn.textContent = '作成中…';
            const cols = COLUMNS.filter(isVisible);
            const aoa = [cols.map(function (c) { return c.label; })];
            shown.forEach(function (r) {
                aoa.push(cols.map(function (c) { return c.xlsx(r); }));
            });
            exportXlsx(aoa, cols.map(function (c) { return c.width; }), opt.exportName).then(function () {
                btn.disabled = false;
                btn.textContent = 'Excel出力';
            });
        });

        syncNavbarOffset();
        window.addEventListener('resize', syncNavbarOffset);
        render();
        return api;
    };

    window.FleetList = { create: create, exportXlsx: exportXlsx, syncNavbarOffset: syncNavbarOffset };
})(window);

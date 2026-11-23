// 複数選択のプルダウン（以前使っていた bootstrap-multiselect 0.9.15 の代わり。jQuery を使わない）。
// 作る DOM は bootstrap-multiselect と同じ形・同じクラス名なので、search.css の見た目がそのまま効く。
// 開け閉めは jf-ui.js のドロップダウン（data-toggle="dropdown"）に任せる。
//
//   const ms = JfMultiSelect.create(selectElement, {
//       nonSelectedText: '指定しない',   // 何も選んでいないとき
//       nSelectedText: ' 件を選択',      // numberDisplayed を超えたとき「N 件を選択」
//       allSelectedText: 'すべて',        // 全部選んだとき（複数選択だけ）
//       numberDisplayed: 2,              // この数までは名前を並べる
//       buttonClass: 'sx-ms',
//       maxHeight: 360,                  // 一覧の高さの上限（px）
//       clickableGroups: true,           // グループ名を押すとまとめて選ぶ／外す
//       collapsibleGroups: true,         // グループの右の ▼ で畳む
//       collapseGroupsByDefault: false,  // 最初から畳んでおく
//       filter: true, filterPlaceholder: '絞り込み'
//   });
//   ms.setSelected(['A', 'B'])   // 選び直す（change は起こさない。bootstrap-multiselect の select と同じ）
//   ms.rebuild()                 // <select> の選択肢を入れ替えたあとに作り直す
//   ms.refresh()                 // <select> の選択状態を見た目に写す
//
// 選んだ値は元の <select> に入れ、人が選び直したときだけネイティブの change（bubbles）を起こす。
(function () {
    'use strict';

    let seq = 0;

    // 行にフォーカスがあるとき Enter / Space で選ぶ（上下キーでの移動は jf-ui.js のドロップダウンがする）
    const keyToggle = function (a, input) {
        a.addEventListener('keydown', function (e) {
            if (e.target !== a || (e.key !== 'Enter' && e.key !== ' ')) { return; }
            e.preventDefault();
            input.click();
        });
    };

    const el = function (tag, className, attrs) {
        const e = document.createElement(tag);
        if (className) { e.className = className; }
        Object.keys(attrs || {}).forEach(function (k) { e.setAttribute(k, attrs[k]); });
        return e;
    };

    const create = function (select, options) {
        const opt = Object.assign({
            nonSelectedText: 'None selected',
            nSelectedText: ' selected',
            allSelectedText: 'All selected',
            numberDisplayed: 3,
            delimiter: ', ',
            buttonClass: '',
            maxHeight: 0,
            clickableGroups: false,
            collapsibleGroups: false,
            collapseGroupsByDefault: false,
            filter: false,
            filterPlaceholder: ''
        }, options);
        const multiple = select.multiple;
        const name = 'jf-ms-' + (++seq);

        // ---- 外側（ボタンと一覧）----
        const wrap = el('div', 'btn-group');
        const button = el('button', ('multiselect dropdown-toggle ' + opt.buttonClass).trim(),
            { type: 'button', 'data-toggle': 'dropdown', 'aria-haspopup': 'true', 'aria-expanded': 'false' });
        const buttonText = el('span', 'multiselect-selected-text');
        button.appendChild(buttonText);
        button.appendChild(document.createTextNode(' '));
        button.appendChild(el('b', 'caret'));
        const ul = el('ul', 'multiselect-container dropdown-menu');
        if (opt.maxHeight) {
            ul.style.maxHeight = opt.maxHeight + 'px';
            ul.style.overflowY = 'auto';
            ul.style.overflowX = 'hidden';
        }
        wrap.appendChild(button);
        wrap.appendChild(ul);
        select.style.display = 'none';
        select.insertAdjacentElement('afterend', wrap);

        // 一覧の中の操作でドロップダウンが閉じないよう、document まで届かせない（jf-ui.js は document の click で閉じる）
        ul.addEventListener('click', function (e) { e.stopPropagation(); });

        // li ⇔ option / グループ の対応
        let items = [];   // { li, input, option, group }
        let groups = [];  // { li, input, items: [], collapsed }
        let filterInput = null;
        let filterText = '';

        const close = function () {
            wrap.classList.remove('open');
            button.setAttribute('aria-expanded', 'false');
        };

        // ---- 見た目を <select> に合わせる ----
        const selectedOptions = function () {
            return Array.from(select.options).filter(function (o) { return o.selected; });
        };

        const updateButton = function () {
            const selected = selectedOptions();
            const texts = selected.map(function (o) { return o.text.trim(); });
            let text;
            if (!multiple) {
                text = texts[0] || opt.nonSelectedText;
            } else if (selected.length === 0) {
                text = opt.nonSelectedText;
            } else if (opt.allSelectedText && selected.length === select.options.length && select.options.length > 1) {
                text = opt.allSelectedText;
            } else if (selected.length > opt.numberDisplayed) {
                text = selected.length + opt.nSelectedText;
            } else {
                text = texts.join(opt.delimiter);
            }
            buttonText.textContent = text;
            // search.css は title を見て「未選択」の色を薄くしている
            button.title = selected.length === 0 ? opt.nonSelectedText : texts.join(opt.delimiter);
        };

        const refresh = function () {
            items.forEach(function (it) {
                it.input.checked = it.option.selected;
                it.li.classList.toggle('active', it.option.selected);
                it.li.classList.toggle('disabled', it.option.disabled);
                it.input.disabled = it.option.disabled;
            });
            groups.forEach(function (g) {
                if (!g.input) { return; }
                const all = g.items.length > 0 && g.items.every(function (it) { return it.option.selected; });
                g.input.checked = all;
                g.li.classList.toggle('active', all);
            });
            updateButton();
        };

        // ---- 絞り込みと畳み ----
        const applyVisibility = function () {
            const q = filterText.toLowerCase();
            items.forEach(function (it) {
                const matched = q === '' || it.option.text.toLowerCase().indexOf(q) >= 0;
                it.li.classList.toggle('multiselect-filter-hidden', !matched);
                // 絞り込み中は畳んだグループの中も見せる
                const collapsed = q === '' && it.group && it.group.collapsed;
                it.li.classList.toggle('multiselect-collapsible-hidden', !!collapsed);
                it.li.style.display = matched && !collapsed ? '' : 'none';
            });
            groups.forEach(function (g) {
                const any = g.items.some(function (it) { return !it.li.classList.contains('multiselect-filter-hidden'); });
                g.li.style.display = any ? '' : 'none';
                g.li.classList.toggle('is-collapsed', g.collapsed);
            });
        };

        const changed = function () {
            refresh();
            select.dispatchEvent(new Event('change', { bubbles: true }));
        };

        // ---- 一覧を作る ----
        const buildOption = function (option, group) {
            const li = el('li');
            const a = el('a', '', { tabindex: '0' });
            const label = el('label', multiple ? 'checkbox' : 'radio');
            const input = el('input', '', { type: multiple ? 'checkbox' : 'radio', name: name, value: option.value });
            label.appendChild(input);
            label.appendChild(document.createTextNode(' ' + option.text));
            a.appendChild(label);
            li.appendChild(a);
            ul.appendChild(li);
            keyToggle(a, input);
            const item ={ li: li, input: input, option: option, group: group };
            input.addEventListener('change', function () {
                if (multiple) {
                    option.selected = input.checked;
                } else {
                    select.value = option.value;
                    close();
                    button.focus();
                }
                changed();
            });
            items.push(item);
            return item;
        };

        const buildGroup = function (optgroup) {
            const group = { li: el('li', 'multiselect-item multiselect-group'), input: null, items: [], collapsed: false };
            const a = el('a', '', { tabindex: '0' });
            const label = el('label');
            if (opt.clickableGroups && multiple) {
                group.li.classList.add('multiselect-group-clickable');
                group.input = el('input', '', { type: 'checkbox' });
                label.appendChild(group.input);
                keyToggle(a, group.input);
                group.input.addEventListener('change', function () {
                    const on = group.input.checked;
                    group.items.forEach(function (it) {
                        // 絞り込みで隠れている行は触らない（bootstrap-multiselect と同じ）
                        if (it.option.disabled || it.li.classList.contains('multiselect-filter-hidden')) { return; }
                        it.option.selected = on;
                    });
                    changed();
                });
            }
            const b = el('b');
            b.textContent = ' ' + optgroup.label;
            label.appendChild(b);
            a.appendChild(label);
            if (opt.collapsibleGroups && multiple) {
                const caret = el('span', 'caret-container', { role: 'button', 'aria-label': optgroup.label + 'を開く／畳む' });
                caret.appendChild(el('b', 'caret'));
                caret.addEventListener('click', function (e) {
                    e.preventDefault();
                    group.collapsed = !group.collapsed;
                    applyVisibility();
                });
                a.appendChild(caret);
                group.collapsed = !!opt.collapseGroupsByDefault;
            }
            group.li.appendChild(a);
            ul.appendChild(group.li);
            groups.push(group);
            Array.from(optgroup.children).forEach(function (option) {
                if (option.tagName === 'OPTION') { group.items.push(buildOption(option, group)); }
            });
        };

        const rebuild = function () {
            ul.replaceChildren();
            items = [];
            groups = [];
            if (opt.filter) {
                const li = el('li', 'multiselect-item multiselect-filter');
                const box = el('div', 'input-group');
                filterInput = el('input', 'form-control multiselect-search', { type: 'text', placeholder: opt.filterPlaceholder });
                filterInput.value = filterText;
                let timer = null;
                filterInput.addEventListener('input', function () {
                    clearTimeout(timer);
                    timer = setTimeout(function () {
                        filterText = filterInput.value.trim();
                        applyVisibility();
                    }, 100);
                });
                // Enter でフォームが送信されないようにする
                filterInput.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); } });
                box.appendChild(filterInput);
                li.appendChild(box);
                ul.appendChild(li);
            }
            Array.from(select.children).forEach(function (child) {
                if (child.tagName === 'OPTGROUP') { buildGroup(child); }
                else if (child.tagName === 'OPTION') { buildOption(child, null); }
            });
            applyVisibility();
            refresh();
        };

        // 開いたら絞り込み欄へ（スマホはキーボードが出て一覧が隠れるのでしない）
        button.addEventListener('click', function () {
            setTimeout(function () {
                if (wrap.classList.contains('open') && filterInput && window.matchMedia('(min-width: 768px)').matches) {
                    filterInput.focus();
                }
            }, 0);
        });

        rebuild();

        return {
            setSelected: function (values) {
                const list = (Array.isArray(values) ? values : [values]).map(String);
                if (multiple) {
                    Array.from(select.options).forEach(function (o) { o.selected = list.indexOf(o.value) >= 0; });
                } else if (Array.from(select.options).some(function (o) { return o.value === list[0]; })) {
                    select.value = list[0];
                }
                refresh();
            },
            rebuild: rebuild,
            refresh: refresh,
            close: close
        };
    };

    window.JfMultiSelect = { create: create };
})();

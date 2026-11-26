const openDetail = function (reg) {
    if (window.innerWidth < 700) {
        window.open('/ADNB/' + reg);
    } else {
        openPopUp('/ADN/' + reg)
    }
}

// 機体詳細などを iframe で重ねて開く。<dialog> を 1 つだけ作って使い回す。
// Esc と背景クリック、右上の × で閉じる。閉じたら iframe を空にして、裏で動き続けないようにする。
const openPopUp = function (url) {
    let dialog = document.getElementById('jfPopup');
    if (!dialog) {
        dialog = document.createElement('dialog');
        dialog.id = 'jfPopup';
        dialog.className = 'jf-popup';
        dialog.innerHTML = '<button type="button" class="jf-popup-close" aria-label="閉じる">&times;</button><iframe title="詳細"></iframe>';
        dialog.querySelector('.jf-popup-close').addEventListener('click', function () { dialog.close(); });
        // dialog 自身がクリックされた = iframe の外 (背景か、× の並ぶ上の余白)
        dialog.addEventListener('click', function (e) { if (e.target === dialog) { dialog.close(); } });
        dialog.addEventListener('close', function () {
            dialog.querySelector('iframe').src = 'about:blank';
            document.documentElement.classList.remove('jf-popup-open');
        });
        document.body.appendChild(dialog);
    }
    dialog.querySelector('iframe').src = url;
    document.documentElement.classList.add('jf-popup-open');
    dialog.showModal();
}

// 「戻る」ボタン用。openPopUp で重ねて開かれた画面の中なら、親のダイアログを閉じる。
// iframe の中で history.back() を呼ぶと、親の画面ごと前のページへ戻ってしまうため。そうでなければ前のページへ戻る
const jfBack = function () {
    try {
        const dialog = window.parent !== window ? window.parent.document.getElementById('jfPopup') : null;
        if (dialog && dialog.open) {
            dialog.close();
            return;
        }
    } catch (e) {
        // 別オリジンの親には触れない（その場合は普通に戻る）
    }
    history.back();
}

// レジ入力欄に、登録済みレジの候補 (<datalist>) を付ける。
// 空欄のまま候補を全部出しても選べないので、1 文字以上入っているときだけ list を付ける。
const attachRegList = function (input) {
    if (!input) { return; }
    fetch('/Master/GetAllReg', { cache: 'no-store' })
        .then(function (res) { return res.ok ? res.json() : []; })
        .then(function (regs) {
            const list = document.createElement('datalist');
            list.id = input.id + 'RegList';
            regs.forEach(function (reg) {
                const option = document.createElement('option');
                option.value = reg;
                list.appendChild(option);
            });
            document.body.appendChild(list);
            const sync = function () {
                if (input.value.trim() === '') {
                    input.removeAttribute('list');
                } else if (input.getAttribute('list') !== list.id) {
                    input.setAttribute('list', list.id);
                }
            };
            input.addEventListener('input', sync);
            sync();
        })
        .catch(function () { /* 候補が出ないだけなので黙って諦める */ });
}

// ==================== 通信 ($.ajax の代わり) ====================
// JfHttp.get(url, params) / JfHttp.post(url, data) / JfHttp.send(method, url, data)
//   - data / params はふつうのオブジェクト。フォーム形式 (x-www-form-urlencoded) で送る。
//     値が配列ならキーを繰り返す (a=1&a=2)。null / undefined は送らない
//   - FormData や URLSearchParams はそのまま送る
//   - 返事は Content-Type が JSON なら JSON、それ以外は文字列で返す ($.ajax と同じ)
//   - 2xx 以外と通信の失敗は reject する。理由のオブジェクトは jqXHR と同じく
//     status / statusText / responseText を持つので、これまでの「失敗しました (404 Not Found)」の出し方がそのまま使える
//   - ブラウザのキャッシュは使わない ($.ajax の cache: false 相当)
const JfHttp = (function () {
    const toParams = function (data) {
        if (data instanceof URLSearchParams) { return data; }
        const params = new URLSearchParams();
        Object.keys(data || {}).forEach(function (key) {
            const value = data[key];
            if (value === null || value === undefined) { return; }
            (Array.isArray(value) ? value : [value]).forEach(function (v) { params.append(key, v); });
        });
        return params;
    };

    const send = function (method, url, data) {
        method = (method || 'GET').toUpperCase();
        const init = { method: method, cache: 'no-store', credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } };
        if (method === 'GET' || method === 'HEAD') {
            const query = toParams(data).toString();
            if (query) { url += (url.indexOf('?') >= 0 ? '&' : '?') + query; }
        } else if (data instanceof FormData) {
            init.body = data;
        } else {
            init.body = toParams(data);
        }
        return fetch(url, init).then(function (res) {
            const type = res.headers.get('Content-Type') || '';
            return res.text().then(function (text) {
                if (!res.ok) {
                    return Promise.reject({ status: res.status, statusText: res.statusText, responseText: text });
                }
                return type.indexOf('json') >= 0 && text !== '' ? JSON.parse(text) : text;
            });
        }, function () {
            return Promise.reject({ status: 0, statusText: 'error', responseText: '' });
        });
    };

    return {
        send: send,
        get: function (url, params) { return send('GET', url, params); },
        post: function (url, data) { return send('POST', url, data); }
    };
})();

// ==================== DOM の小物 (jQuery の show / hide / trigger の代わり) ====================
// el は要素か、要素の並び (NodeList / 配列)。null は何もしない
const JfDom = (function () {
    const each = function (el, fn) {
        if (!el) { return; }
        if (el.nodeType === 1) { fn(el); return; }
        Array.prototype.forEach.call(el, fn);
    };
    const INLINE = /^(A|SPAN|LABEL|B|I|EM|STRONG|SMALL|CODE|IMG|ABBR)$/;
    // .show() と同じく、style で消していればそれを外し、CSS で消えているなら既定の表示にする
    const show = function (el) {
        each(el, function (e) {
            e.style.display = '';
            if (getComputedStyle(e).display === 'none') {
                e.style.display = INLINE.test(e.tagName) ? 'inline' : 'block';
            }
        });
    };
    const hide = function (el) { each(el, function (e) { e.style.display = 'none'; }); };
    return {
        // $(function () { ... }) の代わり。ページの最後のスクリプトまで読み終わってから動く
        ready: function (fn) {
            if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', fn); } else { fn(); }
        },
        show: show,
        hide: hide,
        toggle: function (el, on) { if (on) { show(el); } else { hide(el); } },
        visible: function (el) { return !!el && getComputedStyle(el).display !== 'none'; },
        // ネイティブのイベントを起こす。jQuery で付けた処理にも届く
        trigger: function (el, type) { each(el, function (e) { e.dispatchEvent(new Event(type, { bubbles: true })); }); },
        // HTML の文字列から要素を 1 つ作る
        create: function (html) {
            const t = document.createElement('template');
            t.innerHTML = html.trim();
            return t.content.firstElementChild;
        }
    };
})();

const sendMessageToMe = function () {
    const field = function (id) { return document.getElementById(id); };
    const result = field('result');
    JfHttp.post('/Message/Send', { name: field('uname').value, reply: field('replyto').value, message: field('message').value })
        .then(function (data) {
            if (data == "OK") {
                field('uname').value = '';
                field('replyto').value = '';
                field('message').value = '';
                result.textContent = '送信しました';
            } else {
                result.textContent = '送信に失敗しました';
            }
        }, function () {
            // 以前から通信に失敗しても「送信しました」と出している
            result.textContent = '送信しました';
        });
}

const ZGB = function() {
    var s = "4+T06//>g899,9AU7/", r = "";
    for (i = 0; i < s.length; i++)r += String.fromCharCode((s.charCodeAt(i) + 21) % 93 + 33);
    return r;
}

// ==================== 確認・お知らせ・入力のポップアップ (alert / confirm / prompt の代わり) ====================
// ブラウザの alert / confirm / prompt は使わない（見た目が揃わず、ブラウザによっては「このページのダイアログを止める」も出るため）。
// どれも Promise を返す。<dialog> を 1 つ作って使い回し、重なって呼ばれたら順に出す。
//   JfDialog.alert('保存しました')                                           → 閉じたら解決
//   JfDialog.confirm('削除しますか？', { okText: '削除する', danger: true }) → true / false
//   JfDialog.prompt('名前', { value: '', placeholder: '例：…' })             → 入れた文字列（前後の空白は除く）/ やめたら null
// 共通の options: title（見出し）, note（下の小さい補足）, okText, cancelText, danger（OK を赤にする）
// 文中の改行はそのまま改行で出る。Esc・背景のクリックは「キャンセル」と同じ（alert は閉じるだけ）。
//
// 画面に書くだけで確認を出すには、ボタン・リンク・フォームに data-confirm="文" を付ける
// （data-confirm-title / data-confirm-note / data-confirm-ok / data-confirm-danger="true" も使える）。
// OK のあとは同じボタンを押し直す・同じフォームを送り直すので、formaction や name/value もそのまま送られる。
const JfDialog = (function () {
    let dialog = null;
    let queue = Promise.resolve();

    const build = function () {
        dialog = document.createElement('dialog');
        dialog.className = 'jf-dlg';
        dialog.innerHTML =
            '<form method="dialog" class="jf-dlg-box">'
            + '<div class="jf-dlg-title"></div>'
            + '<div class="jf-dlg-msg"></div>'
            + '<input type="text" class="jf-dlg-input" autocomplete="off" />'
            + '<div class="jf-dlg-note"></div>'
            + '<div class="jf-dlg-foot">'
            + '<button type="submit" value="cancel" class="jf-dlg-btn jf-dlg-cancel"></button>'
            + '<button type="submit" value="ok" class="jf-dlg-btn jf-dlg-ok"></button>'
            + '</div></form>';
        // 背景（dialog 自身）を押したらキャンセル
        dialog.addEventListener('click', function (e) { if (e.target === dialog) { dialog.close('cancel'); } });
        document.body.appendChild(dialog);
    };

    const show = function (kind, message, opt) {
        opt = opt || {};
        if (!dialog) { build(); }
        const q = function (sel) { return dialog.querySelector(sel); };
        const setText = function (sel, text) {
            const el = q(sel);
            el.textContent = text || '';
            el.hidden = !text;
        };
        setText('.jf-dlg-title', opt.title);
        setText('.jf-dlg-msg', message);
        setText('.jf-dlg-note', opt.note);
        const input = q('.jf-dlg-input');
        const ok = q('.jf-dlg-ok');
        const cancel = q('.jf-dlg-cancel');
        input.hidden = kind !== 'prompt';
        input.value = opt.value || '';
        input.placeholder = opt.placeholder || '';
        input.maxLength = opt.maxLength || 524288;
        ok.textContent = opt.okText || 'OK';
        ok.classList.toggle('danger', !!opt.danger);
        cancel.textContent = opt.cancelText || 'キャンセル';
        cancel.hidden = kind === 'alert';
        // prompt は空のままでは OK を押せない
        const syncOk = function () { ok.disabled = kind === 'prompt' && input.value.trim() === ''; };
        input.oninput = syncOk;
        syncOk();

        return new Promise(function (resolve) {
            dialog.returnValue = '';
            dialog.addEventListener('close', function () {
                const accepted = dialog.returnValue === 'ok';
                if (kind === 'alert') { resolve(); }
                else if (kind === 'confirm') { resolve(accepted); }
                else { resolve(accepted ? input.value.trim() : null); }
            }, { once: true });
            dialog.showModal();
            (kind === 'prompt' ? input : ok).focus();
            if (kind === 'prompt') { input.select(); }
        });
    };

    // 重なって呼ばれても 1 つずつ出す
    const enqueue = function (kind, message, opt) {
        const p = queue.then(function () { return show(kind, message, opt); });
        queue = p.catch(function () { });
        return p;
    };

    // ---- data-confirm（画面に書くだけの確認） ----
    const optionsOf = function (el) {
        return {
            title: el.getAttribute('data-confirm-title'),
            note: el.getAttribute('data-confirm-note'),
            okText: el.getAttribute('data-confirm-ok'),
            danger: el.getAttribute('data-confirm-danger') === 'true'
        };
    };
    // ボタン・リンク: 1 回目の押下を止めて確認し、OK なら同じものを押し直す（2 回目は素通り）
    document.addEventListener('click', function (e) {
        const el = e.target.closest ? e.target.closest('[data-confirm]') : null;
        if (!el || el.tagName === 'FORM') { return; }
        if (el.jfConfirmed) { el.jfConfirmed = false; return; }
        e.preventDefault();
        e.stopImmediatePropagation();
        enqueue('confirm', el.getAttribute('data-confirm'), optionsOf(el)).then(function (yes) {
            if (yes) { el.jfConfirmed = true; el.click(); }
        });
    }, true);
    // フォーム: 送信を止めて確認し、OK なら押したボタンのまま送り直す
    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!form.hasAttribute || !form.hasAttribute('data-confirm')) { return; }
        if (form.jfConfirmed) { form.jfConfirmed = false; return; }
        e.preventDefault();
        e.stopImmediatePropagation();
        const submitter = e.submitter;
        enqueue('confirm', form.getAttribute('data-confirm'), optionsOf(form)).then(function (yes) {
            if (!yes) { return; }
            form.jfConfirmed = true;
            if (form.requestSubmit) { form.requestSubmit(submitter && submitter.form === form ? submitter : undefined); }
            else { form.submit(); }
        });
    }, true);

    return {
        alert: function (message, opt) { return enqueue('alert', message, opt); },
        confirm: function (message, opt) { return enqueue('confirm', message, opt); },
        prompt: function (message, opt) { return enqueue('prompt', message, opt); }
    };
})();

const getParam = function(name, url) {
    if (!url) url = window.location.href;
    name = name.replace(/[\[\]]/g, "\\$&");
    var regex = new RegExp("[?&]" + name + "(=([^&#]*)|&|#|$)"),
    results = regex.exec(url);
    if (!results) return null;
    if (!results[2]) return '';
    return decodeURIComponent(results[2].replace(/\+/g, " "));
};
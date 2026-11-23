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

const sendMessageToMe = function() {
    $.ajax({
        type: 'POST',
        url: '/Message/Send',
        data: { name: $('#uname').val(), reply: $('#replyto').val(), message: $('#message').val() },
        cache: false
    }).fail(function (data) {
        $('#result').text("送信しました");
    }).done(function (data) {
        if (data == "OK") {
            $('#uname').val("");
            $('#replyto').val("");
            $('#message').val("");
            $('#result').text("送信しました");
        } else {
            $('#result').text("送信に失敗しました");
        }
    });
}

const ZGB = function() {
    var s = "4+T06//>g899,9AU7/", r = "";
    for (i = 0; i < s.length; i++)r += String.fromCharCode((s.charCodeAt(i) + 21) % 93 + 33);
    return r;
}

const getParam = function(name, url) {
    if (!url) url = window.location.href;
    name = name.replace(/[\[\]]/g, "\\$&");
    var regex = new RegExp("[?&]" + name + "(=([^&#]*)|&|#|$)"),
    results = regex.exec(url);
    if (!results) return null;
    if (!results[2]) return '';
    return decodeURIComponent(results[2].replace(/\+/g, " "));
};
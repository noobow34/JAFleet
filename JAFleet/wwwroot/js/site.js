const openDetail = function (reg) {
    if (window.innerWidth < 700) {
        window.open('/ADNB/' + reg);
    } else {
        openPopUp('/ADN/' + reg)
    }
}

const openPopUp = function(url){
    $.magnificPopup.open({
        items: { src: url },
        type: 'iframe'
    });
    let setwidth = window.innerWidth * 0.95;
    if (setwidth > 1200) {
        setwidth = 1200;
    }
    $('.mfp-iframe-holder .mfp-content').css({ 'height': '95vh' });
    $('.mfp-iframe-holder .mfp-content').css({ 'max-width': setwidth + 'px' });
    $('.mfp-iframe-holder .mfp-content').css({ 'width': setwidth + 'px' });
}

const sendMessageToMe = function() {
    $.ajax({
        type: 'POST',
        url: '/Message/Send',
        data: { name: $('#uname').val(), replay: $('#replayto').val(), message: $('#message').val() },
        cache: false
    }).fail(function (data) {
        $('#result').text("送信しました");
    }).done(function (data) {
        if (data == "OK") {
            $('#uname').val("");
            $('#replayto').val("");
            $('#message').val("");
            $('#result').text("送信しました");
        } else {
            $('#result').text("送信に失敗しました");
        }
    });
}

const datatablesLangInit = function() {
    $.extend($.fn.dataTable.defaults, {
        language: {
            url: "https://cdn.datatables.net/plug-ins/9dcbecd42ad/i18n/Japanese.json"
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
}
// Bootstrap 3 の JS (modal / collapse / dropdown) の代わり。jQuery を使わない。
// マークアップと CSS は Bootstrap 3 のまま (.modal / .in / .collapse / .collapsing / .open) で動くようにしてある。
//
//   JfUi.modal.show(target) / JfUi.modal.hide(target)
//   JfUi.collapse.show(target) / hide(target) / toggle(target)
//   target は要素・セレクタ文字列・jQuery オブジェクトのどれでもよい
//
// 発生するイベント (要素に対して、bubbles):
//   jf:modal-show (preventDefault で止められる) / jf:modal-shown / jf:modal-hide / jf:modal-hidden
//   jf:collapse-show / jf:collapse-shown / jf:collapse-hide / jf:collapse-hidden
//
// data 属性はこれまでどおり使える:
//   data-toggle="modal" data-target="#id"  /  data-dismiss="modal"
//   data-toggle="collapse" data-target="#id"
//   data-toggle="dropdown" (親要素に .open を付け外しする)
(function () {
    'use strict';

    const MODAL_TRANSITION = 300;
    const COLLAPSE_TRANSITION = 350;

    const resolve = function (target) {
        if (typeof target === 'string') { return document.querySelector(target); }
        if (target && target.jquery) { return target[0]; }
        return target || null;
    };

    const fire = function (el, name, cancelable) {
        const ev = new CustomEvent(name, { bubbles: true, cancelable: !!cancelable });
        el.dispatchEvent(ev);
        return ev;
    };

    const reflow = function (el) { return el.offsetHeight; };

    // ==================== modal ====================
    // 同時に開くのは 1 つだけ (Bootstrap 3 と同じく重ねては使っていない)
    let current = null; // { el, backdrop, bodyPad, hiding, timer }

    const scrollbarWidth = function () {
        const measure = document.createElement('div');
        measure.className = 'modal-scrollbar-measure';
        document.body.appendChild(measure);
        const width = measure.offsetWidth - measure.clientWidth;
        document.body.removeChild(measure);
        return width;
    };

    // 閉じる途中のものを、アニメーションを待たずに片付ける
    const finishHide = function () {
        if (!current) { return; }
        const c = current;
        current = null;
        clearTimeout(c.timer);
        c.el.style.display = 'none';
        c.el.style.paddingLeft = '';
        c.el.style.paddingRight = '';
        c.el.classList.remove('in');
        if (c.backdrop.parentNode) { c.backdrop.parentNode.removeChild(c.backdrop); }
        document.body.classList.remove('modal-open');
        document.body.style.paddingRight = c.bodyPad;
        fire(c.el, 'jf:modal-hidden');
    };

    const showModal = function (target) {
        const el = resolve(target);
        if (!el) { return; }
        if (current && current.el === el && !current.hiding) { return; }
        if (fire(el, 'jf:modal-show', true).defaultPrevented) { return; }
        finishHide();

        // 本文のスクロールバーが消えて横にずれないよう、その幅だけ右に余白を足す (Bootstrap 3 と同じ)
        const body = document.body;
        const bodyOverflowing = body.clientWidth < window.innerWidth;
        const sbWidth = scrollbarWidth();
        const bodyPad = body.style.paddingRight;
        if (bodyOverflowing) {
            body.style.paddingRight = ((parseFloat(getComputedStyle(body).paddingRight) || 0) + sbWidth) + 'px';
        }
        body.classList.add('modal-open');

        const backdrop = document.createElement('div');
        backdrop.className = 'modal-backdrop fade';
        body.appendChild(backdrop);

        el.style.display = 'block';
        el.scrollTop = 0;
        const modalOverflowing = el.scrollHeight > document.documentElement.clientHeight;
        el.style.paddingLeft = !bodyOverflowing && modalOverflowing ? sbWidth + 'px' : '';
        el.style.paddingRight = bodyOverflowing && !modalOverflowing ? sbWidth + 'px' : '';

        reflow(el);
        backdrop.classList.add('in');
        el.classList.add('in');

        current = { el: el, backdrop: backdrop, bodyPad: bodyPad, hiding: false, timer: 0 };
        const mine = current;
        mine.timer = setTimeout(function () {
            if (current !== mine) { return; }
            el.focus();
            fire(el, 'jf:modal-shown');
        }, MODAL_TRANSITION);
    };

    const hideModal = function (target) {
        const el = resolve(target);
        if (!el || !current || current.el !== el || current.hiding) { return; }
        if (fire(el, 'jf:modal-hide', true).defaultPrevented) { return; }
        const mine = current;
        mine.hiding = true;
        clearTimeout(mine.timer);
        el.classList.remove('in');
        mine.backdrop.classList.remove('in');
        mine.timer = setTimeout(function () {
            if (current === mine) { finishHide(); }
        }, MODAL_TRANSITION);
    };

    // 背景 (= .modal 自身) のクリックで閉じる。
    // ダイアログの中で押してから外で離したとき (文字の選択など) は閉じない
    let downInDialog = false;
    document.addEventListener('mousedown', function (e) {
        downInDialog = !!(current && e.target !== current.el && current.el.contains(e.target));
    });
    document.addEventListener('click', function (e) {
        if (current && !current.hiding && e.target === current.el && !downInDialog) {
            hideModal(current.el);
        }
        downInDialog = false;
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && current && !current.hiding) {
            hideModal(current.el);
        }
    });

    // 開いているあいだはフォーカスをダイアログの外に出さない
    document.addEventListener('focusin', function (e) {
        if (current && !current.hiding && e.target !== document && !current.el.contains(e.target)) {
            current.el.focus();
        }
    });

    // ==================== collapse ====================
    const collapseTriggers = function (el) {
        if (!el.id) { return []; }
        return Array.prototype.slice.call(document.querySelectorAll(
            '[data-toggle="collapse"][data-target="#' + el.id + '"], [data-toggle="collapse"][href="#' + el.id + '"]'));
    };

    const setTriggers = function (el, open) {
        collapseTriggers(el).forEach(function (t) {
            t.classList.toggle('collapsed', !open);
            t.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
        el.setAttribute('aria-expanded', open ? 'true' : 'false');
    };

    const showCollapse = function (target) {
        const el = resolve(target);
        if (!el || el.classList.contains('in') || el.classList.contains('collapsing')) { return; }
        if (fire(el, 'jf:collapse-show', true).defaultPrevented) { return; }
        el.classList.remove('collapse');
        el.classList.add('collapsing');
        el.style.height = '0px';
        setTriggers(el, true);
        reflow(el);
        el.style.height = el.scrollHeight + 'px';
        setTimeout(function () {
            el.classList.remove('collapsing');
            el.classList.add('collapse', 'in');
            el.style.height = '';
            fire(el, 'jf:collapse-shown');
        }, COLLAPSE_TRANSITION);
    };

    const hideCollapse = function (target) {
        const el = resolve(target);
        if (!el || !el.classList.contains('in') || el.classList.contains('collapsing')) { return; }
        if (fire(el, 'jf:collapse-hide', true).defaultPrevented) { return; }
        el.style.height = el.getBoundingClientRect().height + 'px';
        reflow(el);
        el.classList.add('collapsing');
        el.classList.remove('collapse', 'in');
        setTriggers(el, false);
        el.style.height = '0px';
        setTimeout(function () {
            el.classList.remove('collapsing');
            el.classList.add('collapse');
            el.style.height = '';
            fire(el, 'jf:collapse-hidden');
        }, COLLAPSE_TRANSITION);
    };

    const toggleCollapse = function (target) {
        const el = resolve(target);
        if (!el) { return; }
        if (el.classList.contains('in')) { hideCollapse(el); } else { showCollapse(el); }
    };

    // ==================== dropdown ====================
    const dropdownToggles = function () {
        return Array.prototype.slice.call(document.querySelectorAll('[data-toggle="dropdown"]'));
    };

    const closeDropdowns = function (except) {
        dropdownToggles().forEach(function (t) {
            const parent = t.parentElement;
            if (parent !== except && parent.classList.contains('open')) {
                parent.classList.remove('open');
                t.setAttribute('aria-expanded', 'false');
                const backdrop = parent.querySelector(':scope > .dropdown-backdrop');
                if (backdrop) { parent.removeChild(backdrop); }
            }
        });
    };

    const openDropdown = function (toggle) {
        const parent = toggle.parentElement;
        // iOS は何もない所を押しても click が document まで来ないので、
        // タッチ端末では画面全体を覆う透明な板を敷いて、それを押したら閉じる (Bootstrap 3 と同じ .dropdown-backdrop)
        if ('ontouchstart' in document.documentElement) {
            const backdrop = document.createElement('div');
            backdrop.className = 'dropdown-backdrop';
            backdrop.addEventListener('click', function () { closeDropdowns(); });
            toggle.insertAdjacentElement('afterend', backdrop);
        }
        parent.classList.add('open');
        toggle.setAttribute('aria-expanded', 'true');
        toggle.focus();
    };

    document.addEventListener('keydown', function (e) {
        const parent = e.target.closest ? e.target.closest('.open') : null;
        if (!parent) { return; }
        const toggle = parent.querySelector('[data-toggle="dropdown"]');
        if (!toggle) { return; }
        if (e.key === 'Escape') {
            closeDropdowns();
            toggle.focus();
            return;
        }
        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') { return; }
        // 上下キーで中のリンクを移る (Bootstrap 3 と同じ)
        const items = Array.prototype.slice.call(parent.querySelectorAll('.dropdown-menu a'))
            .filter(function (a) { return a.offsetParent !== null; });
        if (items.length === 0) { return; }
        e.preventDefault();
        let index = items.indexOf(document.activeElement);
        if (e.key === 'ArrowDown') { index = index < items.length - 1 ? index + 1 : index; }
        if (e.key === 'ArrowUp') { index = index > 0 ? index - 1 : 0; }
        if (index < 0) { index = 0; }
        items[index].focus();
    });

    // ==================== data 属性 ====================
    document.addEventListener('click', function (e) {
        if (!e.target.closest) { return; }

        const dismiss = e.target.closest('[data-dismiss="modal"]');
        if (dismiss) {
            hideModal(dismiss.closest('.modal'));
            return;
        }

        const toggle = e.target.closest('[data-toggle]');
        const kind = toggle ? toggle.getAttribute('data-toggle') : null;

        if (kind === 'dropdown') {
            if (toggle.tagName === 'A') { e.preventDefault(); }
            const wasOpen = toggle.parentElement.classList.contains('open');
            closeDropdowns();
            if (!wasOpen) { openDropdown(toggle); }
            return;
        }

        // ほかの所を押したら開いているドロップダウンを閉じる (Bootstrap 3 と同じく click で見る)。
        // 中の入力欄を押したときだけは閉じない。bootstrap-multiselect は中の項目の click を自分で止めている
        const openParent = e.target.closest('.open');
        const typing = openParent && /^(INPUT|TEXTAREA)$/.test(e.target.tagName);
        closeDropdowns(typing ? openParent : null);

        if (kind !== 'modal' && kind !== 'collapse') { return; }
        if (toggle.tagName === 'A') { e.preventDefault(); }

        const selector = toggle.getAttribute('data-target') || toggle.getAttribute('href');
        if (!selector || selector === '#') { return; }
        if (kind === 'modal') { showModal(selector); } else { toggleCollapse(selector); }
    });

    window.JfUi = {
        modal: { show: showModal, hide: hideModal },
        collapse: { show: showCollapse, hide: hideCollapse, toggle: toggleCollapse }
    };
})();

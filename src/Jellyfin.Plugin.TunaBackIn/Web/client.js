/* TunaBackIn: shows "Continue music streaming of X" in jellyfin-web after a video ends. */
(function () {
    'use strict';
    if (window.TunaBackIn) {
        return;
    }

    var POLL_MS = 5000;
    var ID = 'tunaBackInPrompt';
    var shownKey = null;
    var busy = false;

    function client() {
        var c = window.ApiClient;
        return c && typeof c.accessToken === 'function' && c.accessToken() ? c : null;
    }

    function request(method, action) {
        var c = client();
        if (!c) {
            return Promise.resolve(null);
        }
        var url = c.getUrl('TunaBackIn/' + action, { deviceId: c.deviceId() });
        return fetch(url, {
            method: method,
            headers: { Authorization: 'MediaBrowser Token="' + c.accessToken() + '"' }
        });
    }

    function hide() {
        var el = document.getElementById(ID);
        if (el) {
            el.remove();
        }
        shownKey = null;
    }

    function button(label, primary, onClick) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'raised emby-button' + (primary ? ' button-submit' : '');
        b.textContent = label;
        b.style.margin = '0 .5em';
        b.addEventListener('click', onClick);
        return b;
    }

    function show(offer, actions) {
        var key = offer.Title + '|' + offer.ItemCount + '|' + offer.CurrentIndex;
        if (shownKey === key && document.getElementById(ID)) {
            return;
        }
        hide();
        shownKey = key;

        var box = document.createElement('div');
        box.id = ID;
        box.className = 'focuscontainer-x';
        box.setAttribute('role', 'dialog');
        box.setAttribute('aria-live', 'polite');
        box.style.cssText = [
            'position:fixed', 'left:50%', 'bottom:6em', 'transform:translateX(-50%)', 'z-index:100000',
            'background:rgba(20,20,20,.95)', 'color:#fff', 'padding:1.2em 1.5em', 'border-radius:.6em',
            'box-shadow:0 .5em 2em rgba(0,0,0,.6)', 'text-align:center', 'max-width:90vw', 'font-size:1.1em'
        ].join(';');

        var text = document.createElement('div');
        text.style.marginBottom = '1em';
        text.textContent = 'Continue music streaming of "' + offer.Title + '"';
        box.appendChild(text);

        var resume = button('Resume', true, actions.resume);
        box.appendChild(resume);
        box.appendChild(button('Dismiss', false, actions.dismiss));
        document.body.appendChild(box);

        // TV remotes navigate by focus; start on Resume.
        setTimeout(function () { resume.focus(); }, 50);
    }

    function poll() {
        if (busy || document.hidden || !client()) {
            return;
        }
        request('GET', 'Offer').then(function (r) {
            if (!r || r.status !== 200) {
                hide();
                return null;
            }
            return r.json();
        }).then(function (offer) {
            if (!offer) {
                return;
            }
            offer = {
                Title: offer.Title || offer.title || 'your music',
                ItemCount: offer.ItemCount || offer.itemCount || 0,
                CurrentIndex: offer.CurrentIndex || offer.currentIndex || 0
            };
            show(offer, {
                resume: function () {
                    busy = true;
                    hide();
                    request('POST', 'Resume').finally(function () { busy = false; });
                },
                dismiss: function () {
                    busy = true;
                    hide();
                    request('DELETE', 'Offer').finally(function () { busy = false; });
                }
            });
        }).catch(function () { /* server unreachable: try again next tick */ });
    }

    window.TunaBackIn = {
        version: 1,
        /** Shows a sample prompt without calling the server; used by the config page to check injection. */
        showTest: function () {
            show({ Title: 'Test album', ItemCount: 0, CurrentIndex: 0 }, { resume: hide, dismiss: hide });
        }
    };

    setInterval(poll, POLL_MS);
    document.addEventListener('visibilitychange', poll);
})();

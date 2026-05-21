(function () {
    'use strict';

    const STORAGE_KEY = 'poproshaykabot.demo.chatSettings.v1';

    const defaults = {
        backgroundColor: { a: 179, r: 0, g: 0, b: 0 },
        textColor: { a: 255, r: 255, g: 255, b: 255 },
        usernameColor: { a: 255, r: 145, g: 70, b: 255 },
        systemMessageColor: { a: 255, r: 255, g: 204, b: 0 },
        timestampColor: { a: 255, r: 153, g: 153, b: 153 },
        fontFamily: '"Motiva Sans", "Inter", "Noto Sans", Arial, sans-serif',
        fontSize: 14,
        fontBold: false,
        padding: 5,
        margin: 5,
        borderRadius: 5,
        animationDuration: 300,
        enableAnimations: true,
        maxMessages: 50,
        showTimestamp: true,
        emoteSizePixels: 28,
        badgeSizePixels: 18,
        showUserAvatars: false,
        userAvatarSizePixels: 32,
        showUserTypeBorders: true,
        highlightFirstTimeUsers: true,
        highlightMentions: true,
        enableMessageShadows: true,
        enableSpecialEffects: true,
        enableSmoothScroll: true,
        scrollAnimationDuration: 300,
        autoScrollEnabled: true,
        scrollToBottomThreshold: 100,
        scrollPauseAfterUserMs: 3000,
        userMessageAnimation: 'slide-in-right',
        botMessageAnimation: 'fade-in-up',
        systemMessageAnimation: 'fade-in-up',
        broadcasterMessageAnimation: 'slide-in-left',
        firstTimeUserMessageAnimation: 'bounce-in',
        enableMessageFadeOut: true,
        messageLifetimeSeconds: 30,
        fadeOutAnimationType: 'fade-out',
        fadeOutAnimationDurationMs: 1000,
    };

    const colorFields = [
        { key: 'backgroundColor', label: 'Фон сообщения' },
        { key: 'textColor', label: 'Основной текст' },
        { key: 'usernameColor', label: 'Имя пользователя' },
        { key: 'systemMessageColor', label: 'Системные' },
        { key: 'timestampColor', label: 'Время' },
    ];

    const cssVarMap = {
        backgroundColor: ['--chat-bg-color', colorToCss],
        textColor: ['--chat-text-color', colorToCss],
        usernameColor: ['--chat-username-color', colorToCss],
        systemMessageColor: ['--chat-system-color', colorToCss],
        timestampColor: ['--chat-timestamp-color', colorToCss],
        fontFamily: ['--chat-font-family', v => v],
        fontSize: ['--chat-font-size', v => v + 'px'],
        padding: ['--chat-padding', v => v + 'px'],
        margin: ['--chat-margin', v => v + 'px 0'],
        borderRadius: ['--chat-border-radius', v => v + 'px'],
        animationDuration: ['--chat-animation-duration', v => v + 'ms'],
        emoteSizePixels: ['--emote-size', v => v + 'px'],
        badgeSizePixels: ['--badge-size', v => v + 'px'],
        userAvatarSizePixels: ['--avatar-size', v => v + 'px'],
        fadeOutAnimationDurationMs: ['--fade-out-duration', v => v + 'ms'],
    };

    const classToggles = [
        ['showUserTypeBorders', '.message', 'no-borders'],
        ['highlightFirstTimeUsers', '.message.first-time', 'no-first-time-effects'],
        ['highlightMentions', '.message', 'no-mentions'],
        ['enableMessageShadows', '.message', 'no-shadows'],
        ['enableSpecialEffects', '.message', 'no-special-effects'],
        ['showTimestamp', '#chat .message, .demo-stage .message', 'no-timestamp'],
        ['showUserAvatars', '.message', 'no-avatars'],
    ];

    const fontWeightVar = '--chat-font-weight';

    const entryAnimList = [
        ['no-animation', 'Без анимации'],
        ['slide-in-right', 'Скольжение справа'],
        ['slide-in-left', 'Скольжение слева'],
        ['fade-in-up', 'Затухание сверху'],
        ['bounce-in', 'Прыжок'],
        ['pop-in', 'Поп-ап с упругостью'],
        ['rubber-band', 'Резиновая лента'],
        ['tada', 'Та-да!'],
        ['zoom-blur-in', 'Кинонаезд с размытием'],
        ['neon-pulse-in', 'Неоновая вспышка'],
        ['materialize', 'Материализация'],
        ['glitch-in', 'Глитч'],
        ['power-up', 'Усиление снизу'],
        ['slam-in', 'Удар сверху'],
        ['notification-bell', 'Звон уведомления'],
        ['hologram', 'Голограмма'],
        ['flip-in-x', 'Переворот по горизонтали'],
        ['roll-in', 'Вкатывание'],
        ['coin-flip', 'Подброс монеты'],
        ['swoop-in', 'Налёт по диагонали'],
    ];

    const exitAnimList = [
        ['no-animation', 'Без анимации'],
        ['fade-out', 'Исчезновение'],
        ['slide-out-left', 'Выскользнуть влево'],
        ['slide-out-right', 'Выскользнуть вправо'],
        ['scale-down', 'Уменьшение'],
        ['shrink-up', 'Свернуться вверх'],
        ['dissolve', 'Растворение в пыль'],
        ['slide-out-up', 'Выскользнуть вверх'],
        ['slide-out-down', 'Выскользнуть вниз'],
        ['rotate-out', 'Поворот с уходом'],
        ['pixelate-out', 'Пикселизация'],
    ];

    function deepClone(value) {
        return JSON.parse(JSON.stringify(value));
    }

    function normalizeColor(raw) {
        if (!raw) return null;
        if (typeof raw === 'string') {
            const hex = raw.startsWith('#') ? raw.slice(1) : raw;
            if (hex.length === 6) {
                return {
                    a: 255,
                    r: parseInt(hex.slice(0, 2), 16),
                    g: parseInt(hex.slice(2, 4), 16),
                    b: parseInt(hex.slice(4, 6), 16),
                };
            }
            if (hex.length === 8) {
                return {
                    a: parseInt(hex.slice(0, 2), 16),
                    r: parseInt(hex.slice(2, 4), 16),
                    g: parseInt(hex.slice(4, 6), 16),
                    b: parseInt(hex.slice(6, 8), 16),
                };
            }
            return null;
        }
        if (typeof raw === 'object') {
            const clamp = (v, d) => {
                const n = Number(v);
                return Number.isFinite(n) ? Math.max(0, Math.min(255, Math.round(n))) : d;
            };
            return {
                a: clamp(raw.a !== undefined ? raw.a : raw.A, 255),
                r: clamp(raw.r !== undefined ? raw.r : raw.R, 0),
                g: clamp(raw.g !== undefined ? raw.g : raw.G, 0),
                b: clamp(raw.b !== undefined ? raw.b : raw.B, 0),
            };
        }
        return null;
    }

    function mergeWithDefaults(incoming) {
        const result = deepClone(defaults);
        if (!incoming || typeof incoming !== 'object') return result;
        Object.keys(result).forEach(key => {
            if (incoming[key] === undefined || incoming[key] === null) return;
            const def = result[key];
            if (def && typeof def === 'object' && 'a' in def) {
                const c = normalizeColor(incoming[key]);
                if (c) result[key] = c;
            } else if (typeof def === 'boolean') {
                result[key] = Boolean(incoming[key]);
            } else if (typeof def === 'number') {
                const n = Number(incoming[key]);
                if (Number.isFinite(n)) result[key] = Math.round(n);
            } else {
                result[key] = String(incoming[key]);
            }
        });
        return result;
    }

    function colorToCss(color) {
        const a = (color.a / 255).toFixed(3).replace(/\.?0+$/, '') || '0';
        return 'rgba(' + color.r + ', ' + color.g + ', ' + color.b + ', ' + a + ')';
    }

    function colorToHex(color) {
        const hex = n => n.toString(16).padStart(2, '0');
        return '#' + hex(color.r) + hex(color.g) + hex(color.b);
    }

    function loadSettings() {
        try {
            const raw = globalThis.localStorage.getItem(STORAGE_KEY);
            if (!raw) return deepClone(defaults);
            return mergeWithDefaults(JSON.parse(raw));
        } catch (e) {
            console.warn('Не удалось прочитать настройки демо из localStorage:', e);
            return deepClone(defaults);
        }
    }

    function saveSettings() {
        try {
            globalThis.localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
        } catch (e) {
            console.warn('Не удалось сохранить настройки демо в localStorage:', e);
        }
    }

    const settings = loadSettings();

    function applySettings() {
        const root = document.documentElement;
        Object.keys(cssVarMap).forEach(key => {
            const entry = cssVarMap[key];
            root.style.setProperty(entry[0], entry[1](settings[key]));
        });
        root.style.setProperty(fontWeightVar, settings.fontBold ? 'bold' : 'normal');

        classToggles.forEach(toggle => {
            const key = toggle[0];
            const selector = toggle[1];
            const className = toggle[2];
            const enabled = settings[key];
            document.querySelectorAll(selector).forEach(el => {
                if (enabled) el.classList.remove(className);
                else el.classList.add(className);
            });
        });
    }

    const drawer = document.getElementById('config-drawer');
    const toggleButton = document.getElementById('config-toggle');
    const closeButton = document.getElementById('config-close');
    const resetButton = document.getElementById('config-reset');
    const pushButton = document.getElementById('config-push');
    const colorsContainer = document.getElementById('config-colors');
    const statusEl = document.getElementById('config-status');

    function buildColorControls() {
        colorsContainer.replaceChildren();
        colorFields.forEach(field => {
            const key = field.key;
            const row = document.createElement('div');
            row.className = 'demo-color-row';

            const labelEl = document.createElement('span');
            labelEl.textContent = field.label;
            row.appendChild(labelEl);

            const swatch = document.createElement('span');
            swatch.className = 'demo-color-swatch';
            swatch.id = 'cfg-' + key + '-swatch';
            row.appendChild(swatch);

            const colorInput = document.createElement('input');
            colorInput.type = 'color';
            colorInput.id = 'cfg-' + key;
            colorInput.title = 'Цвет (RGB)';
            row.appendChild(colorInput);

            const alphaInput = document.createElement('input');
            alphaInput.type = 'range';
            alphaInput.id = 'cfg-' + key + '-alpha';
            alphaInput.min = '0';
            alphaInput.max = '255';
            alphaInput.title = 'Прозрачность (alpha)';
            row.appendChild(alphaInput);

            colorsContainer.appendChild(row);

            const sync = () => {
                const current = settings[key];
                const hex = colorToHex(current);
                if (colorInput.value.toLowerCase() !== hex) colorInput.value = hex;
                if (alphaInput.value !== String(current.a)) alphaInput.value = String(current.a);
                swatch.style.setProperty('--swatch-color', colorToCss(current));
            };

            colorInput.addEventListener('input', () => {
                const hex = colorInput.value.slice(1);
                settings[key] = {
                    a: settings[key].a,
                    r: parseInt(hex.slice(0, 2), 16),
                    g: parseInt(hex.slice(2, 4), 16),
                    b: parseInt(hex.slice(4, 6), 16),
                };
                onChange();
                sync();
            });

            alphaInput.addEventListener('input', () => {
                settings[key] = Object.assign({}, settings[key], { a: parseInt(alphaInput.value, 10) });
                onChange();
                sync();
            });

            sync();
        });
    }

    function populateAnimationSelects() {
        document.querySelectorAll('select[data-kind]').forEach(sel => {
            const list = sel.dataset.kind === 'exit' ? exitAnimList : entryAnimList;
            sel.replaceChildren();
            list.forEach(pair => {
                const opt = document.createElement('option');
                opt.value = pair[0];
                opt.textContent = pair[1];
                sel.appendChild(opt);
            });
        });
    }

    function bindControl(id, settingKey, kind) {
        const el = document.getElementById(id);
        if (!el) return;

        if (kind === 'range' || kind === 'number') {
            if (el.dataset.min !== undefined) el.min = el.dataset.min;
            if (el.dataset.max !== undefined) el.max = el.dataset.max;
            if (el.dataset.step !== undefined) el.step = el.dataset.step;
            el.value = String(settings[settingKey]);
            const valueLabel = document.getElementById(id + '-value');
            const suffix = valueLabel ? valueLabel.dataset.suffix || '' : '';
            const sync = () => {
                if (valueLabel) valueLabel.textContent = settings[settingKey] + suffix;
            };
            sync();
            el.addEventListener('input', () => {
                const n = Number(el.value);
                if (Number.isFinite(n)) {
                    settings[settingKey] = Math.round(n);
                    sync();
                    onChange();
                }
            });
        } else if (kind === 'checkbox') {
            el.checked = Boolean(settings[settingKey]);
            el.addEventListener('change', () => {
                settings[settingKey] = el.checked;
                onChange();
            });
        } else if (kind === 'text') {
            el.value = String(settings[settingKey]);
            el.addEventListener('input', () => {
                settings[settingKey] = el.value;
                onChange();
            });
        } else if (kind === 'select') {
            el.value = String(settings[settingKey]);
            el.addEventListener('change', () => {
                settings[settingKey] = el.value;
                onChange();
            });
        }
    }

    function bindAll() {
        bindControl('cfg-fontFamily', 'fontFamily', 'text');
        bindControl('cfg-fontSize', 'fontSize', 'range');
        bindControl('cfg-fontBold', 'fontBold', 'checkbox');
        bindControl('cfg-padding', 'padding', 'range');
        bindControl('cfg-margin', 'margin', 'range');
        bindControl('cfg-borderRadius', 'borderRadius', 'range');
        bindControl('cfg-maxMessages', 'maxMessages', 'range');
        bindControl('cfg-showTimestamp', 'showTimestamp', 'checkbox');
        bindControl('cfg-emoteSizePixels', 'emoteSizePixels', 'range');
        bindControl('cfg-badgeSizePixels', 'badgeSizePixels', 'range');
        bindControl('cfg-showUserAvatars', 'showUserAvatars', 'checkbox');
        bindControl('cfg-userAvatarSizePixels', 'userAvatarSizePixels', 'range');
        bindControl('cfg-showUserTypeBorders', 'showUserTypeBorders', 'checkbox');
        bindControl('cfg-highlightFirstTimeUsers', 'highlightFirstTimeUsers', 'checkbox');
        bindControl('cfg-highlightMentions', 'highlightMentions', 'checkbox');
        bindControl('cfg-enableMessageShadows', 'enableMessageShadows', 'checkbox');
        bindControl('cfg-enableSpecialEffects', 'enableSpecialEffects', 'checkbox');
        bindControl('cfg-enableSmoothScroll', 'enableSmoothScroll', 'checkbox');
        bindControl('cfg-scrollAnimationDuration', 'scrollAnimationDuration', 'range');
        bindControl('cfg-autoScrollEnabled', 'autoScrollEnabled', 'checkbox');
        bindControl('cfg-scrollToBottomThreshold', 'scrollToBottomThreshold', 'range');
        bindControl('cfg-scrollPauseAfterUserMs', 'scrollPauseAfterUserMs', 'range');
        bindControl('cfg-enableAnimations', 'enableAnimations', 'checkbox');
        bindControl('cfg-animationDuration', 'animationDuration', 'range');
        bindControl('cfg-userMessageAnimation', 'userMessageAnimation', 'select');
        bindControl('cfg-botMessageAnimation', 'botMessageAnimation', 'select');
        bindControl('cfg-systemMessageAnimation', 'systemMessageAnimation', 'select');
        bindControl('cfg-broadcasterMessageAnimation', 'broadcasterMessageAnimation', 'select');
        bindControl('cfg-firstTimeUserMessageAnimation', 'firstTimeUserMessageAnimation', 'select');
        bindControl('cfg-enableMessageFadeOut', 'enableMessageFadeOut', 'checkbox');
        bindControl('cfg-messageLifetimeSeconds', 'messageLifetimeSeconds', 'range');
        bindControl('cfg-fadeOutAnimationType', 'fadeOutAnimationType', 'select');
        bindControl('cfg-fadeOutAnimationDurationMs', 'fadeOutAnimationDurationMs', 'range');
    }

    function refreshControlsFromSettings() {
        Object.keys(settings).forEach(key => {
            const def = defaults[key];
            const isColor = def && typeof def === 'object' && 'a' in def;
            if (isColor) {
                const colorInput = document.getElementById('cfg-' + key);
                const alphaInput = document.getElementById('cfg-' + key + '-alpha');
                const swatch = document.getElementById('cfg-' + key + '-swatch');
                if (colorInput) colorInput.value = colorToHex(settings[key]);
                if (alphaInput) alphaInput.value = String(settings[key].a);
                if (swatch) swatch.style.setProperty('--swatch-color', colorToCss(settings[key]));
                return;
            }
            const el = document.getElementById('cfg-' + key);
            if (!el) return;
            if (el.type === 'checkbox') {
                el.checked = Boolean(settings[key]);
            } else if (el.type === 'range' || el.type === 'number') {
                el.value = String(settings[key]);
                const lbl = document.getElementById(el.id + '-value');
                if (lbl) lbl.textContent = settings[key] + (lbl.dataset.suffix || '');
            } else {
                el.value = String(settings[key]);
            }
        });
    }

    function onChange() {
        applySettings();
        saveSettings();
    }

    function setDrawerOpen(open) {
        if (open) {
            drawer.classList.add('open');
            drawer.setAttribute('aria-hidden', 'false');
            toggleButton.classList.add('active');
            toggleButton.setAttribute('aria-pressed', 'true');
            document.body.classList.add('drawer-open');
        } else {
            drawer.classList.remove('open');
            drawer.setAttribute('aria-hidden', 'true');
            toggleButton.classList.remove('active');
            toggleButton.setAttribute('aria-pressed', 'false');
            document.body.classList.remove('drawer-open');
        }
    }

    toggleButton.addEventListener('click', () => setDrawerOpen(!drawer.classList.contains('open')));
    closeButton.addEventListener('click', () => setDrawerOpen(false));

    resetButton.addEventListener('click', () => {
        Object.assign(settings, deepClone(defaults));
        onChange();
        refreshControlsFromSettings();
        setStatus('Сброшено к умолчаниям.', 'ok');
    });

    pushButton.addEventListener('click', async () => {
        const previousLabel = pushButton.textContent;
        pushButton.disabled = true;
        pushButton.textContent = '… отправка';
        setStatus('Отправка настроек в OBS…', '');
        try {
            const response = await fetch('/api/chat-settings', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(settings),
            });
            if (!response.ok) {
                let detail = '';
                try {
                    const err = await response.json();
                    detail = err && (err.error || err.details) ? ': ' + (err.error || err.details) : '';
                } catch (_) { /* ignore */
                }
                setStatus('Ошибка ' + response.status + detail, 'error');
                return;
            }
            setStatus('Применено к OBS — оверлей чата обновлён через SSE.', 'ok');
        } catch (e) {
            setStatus('Сеть недоступна или бот не запущен: ' + e.message, 'error');
        } finally {
            pushButton.disabled = false;
            pushButton.textContent = previousLabel;
        }
    });

    function setStatus(text, kind) {
        statusEl.textContent = text;
        statusEl.classList.remove('ok', 'error');
        if (kind) statusEl.classList.add(kind);
    }

    populateAnimationSelects();
    buildColorControls();
    bindAll();
    refreshControlsFromSettings();
    applySettings();
}());

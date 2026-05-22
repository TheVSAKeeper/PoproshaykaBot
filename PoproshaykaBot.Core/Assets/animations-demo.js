(function () {
    'use strict';

    let entryAnimations = [];
    let exitAnimations = [];

    const sampleTexts3 = [
        'Серёга Пират ЖЁСТКО вышел в рейд с одним пистолетом.',
        'Чат, это не лаги - это Тарков проверяет характер.',
        'Залутал болт, гайку и минус самооценку.',
        'План был тихий, но Серёга Пират нажал W.',
        'Тарков дал, Тарков забрал, Тарков посмеялся.',
        'Лучший момент стрима: я почти дошёл до выхода.',
        'Килла посмотрел на меня - я посмотрел на экран загрузки.',
        'Это не кемпер, это тактический мыслитель в кустах.',
        'Пошёл за квестом, вернулся с психологической травмой.',
        'Серёга Пират ЖЁСТКО объяснил, почему броня не нужна.',
        'Услышал шаги - это были мои надежды на выживание.',
        'Флешка нашлась ровно после того, как я перестал её искать.',
        'Вышел на Завод, загрузился обратно в меню.',
        'Подписался, поставил плюс, забрал страховку.',
        'Чат, я не умер - я сделал быстрый ребаланс инвентаря.',
        'Серёга Пират бы сказал: “идём по красоте”, и нажал бы на мину.',
        'Лут жирный, руки потные, выход где-то в другой вселенной.',
        'Это просто огонь! Особенно когда граната отскочила обратно.',
        'Пиратский рейд: зашёл бедным, вышел легендой… в схрон.',
        'Дикие сегодня добрые: сразу отправляют домой.',
        'Нашёл выход, но Тарков нашёл меня первым.',
        'Серёга Пират ЖЁСТКО залутал банку тушёнки и смысл жизни.',
        'USEC, BEAR, а я просто человек с ножом и мечтой.',
        'Граната была учебная. Учила смирению.',
        'Тарков - игра, где даже ящик смотрит на тебя с подозрением.',
        'Дошёл до экстракта, но экстракт не дошёл до меня.',
        'Лучший сейв дня: я сохранил спокойствие. Лут - нет.',
        'Чат, это был не миссплей, это авторский контент.',
        'Не помер 40 раз на резерве, а схрон разгрузил.',
    ];

    const sampleTexts = [
        'мб на выход тогда, велью уже забрали',
        'поч не мхлр? план же в тупую и работает',
        'чо ты пикаешь так, тебя любой чвк сотрёт',
        'не регает, неткод подводит',
        'держим газ, пока рейд сам себя не закрыл',
        'зря бегаешь так по карте, ща хуже будет',
        'у тебя выход под рукой, ес вдруг пригодится',
        'не велью оделся, не набит квест',
        'ток не стой афк, до тебя пули и так не долетают',
        'мб лучше лечь в куст и подумать о жизни',
        'смысла мало выходить в прицеле, если не знаешь где тип',
        'пикнул отвратительно, но зато уверенно',
        'можно через awsd выглянуть и сразу назад, а не принимать судьбу лицом',
        'зачем ты выходишь на перезарядке?',
        'угол твой, но голова уже не твоя',
        'по звуку он справа, по таркову он в душе',
        'прицел уводит, из-за этого промахиваешься много',
        'фонарь больше тебя слепит, чем врага',
        'теплак кал, но идея смешная',
        'дым + теплак, и можно изображать мыслительный процесс',
        'какие актуальные задачи на рейд?',
        'базовый минимум выполнен, смываемся в унитаз',
        'короткий квест, всего надо потерять нервную систему',
        'ебаный квест, но пройти всё равно придётся',
        'сюжетка разблокалась, теперь можно страдать официально',
        'разгрузка нужна по квестику какому-то',
        'спавн под квест, значит ща будет не спавн, а цирк',
        'мб на таможню, проходную зону покемпим?',
        'на выход сесть и ждать - тоже геймплей',
        'прост пустой рейд, зато морально стабильный',
        'стату пули глянь, если интересно',
        'свд кал, но в руках уверенного человека всё равно кал',
        'дефолт пристрелка 50 метров, даже калиматор не виноват',
        'настильность у пули подводит',
        'все пули летят в прицел, просто прицел живёт отдельно',
        'мхлр тебе на квест принёс, а ты нос воротишь',
        'а поч не болт? чисто для страдания',
        'магазин проверь, а то опять будет театр одного патрона',
        'сетап найс, только стрелять им кто будет?',
        'броня есть, уверенности нету',
        'ты выжал максимум из этого рейда, ты красавчик',
        'у тебя были все шансы, чуть-чуть не пошло',
        'а был бы там я',
        'получай нахуй, ебаная перила',
        'на нахуй, ебаный камень',
        'показательная распрыжка, показать что дохуя чо можешь',
        'и сразу после неё прыгнуть в стену',
        'прыжок страха или смелости?',
        'чо ты в пвп забыл',
        'как тебе лейт пвп таркова?',
        'держим газ',
        'WWW держим газ',
        'киберспортивный расклад пошёл',
        'велью ноль, зато сдеанонился',
        'на нахуй, получай рундук',
        'не натурально роль снайпера отыгрываешь',
        'мб не терпи, без меня',
        'чо мне заняться нечем, я негатив пвп не хочу',
        'в пве можем зайти потестить',
        'нафик те фигурки, в пве нету престижей',
        'вот бы радарчик, да. пока кемпим на выходе, айда накодим',
        'там не баг, там архитектурная боль',
        'неткод парашный, но звучит как фича',
        'опять кто-то пересчитывает весь список ради одного токена',
        'лайфчекер нужен, а не вот это всё',
        'упёрлось в сеть, поэтому долго конеш работало',
        'можно было кешировать, но зачем, если можно страдать',
        'system io moment',
        'ооп отменяем, переходим на статики, тупа быстрее',
        'дизайн - на твоих руках',
        'мб на выход?',
        'поч не мхлр?',
        'чо за пик?',
        'не регает',
        'держим газ',
        'нету велью',
        'пули кал',
        'теплак кал',
        'квест душит',
        'рейд пустой',
        'чвк не простят',
        'угол плохой',
        'зря вышел',
        'неткод подводит',
        'пве момент',
        'пвп негатив',
        'выход рядом',
        'сетап странный',
        'пик отвратительный',
        'но идея смешная',
        'запрограммировать шрифт ✔✔✔',
        'попытаться ✔✔✔',
        'прыгнуть на месте✔✔✔',
        'взорвать облако✔✔✔',
        'дать гранате физику резинового мяча✔✔✔',
        'захуярить стену✔✔✔',
        'сохранить паштет✔✔✔',
        'нахуй паштет✔✔✔',
        'задымить себе прострел✔✔✔',
        'прыгнуть от пизды (галочка)',
        'захуярь землю нахуй (галочка)',
        'сделать круг вокруг тачки (галочка)',
        'показательная распрыжка, показать что дохуя чо можешь (галочка)',
    ];

    let currentRole = 'broadcaster';

    const entryGrid = document.getElementById('entry-grid');
    const exitGrid = document.getElementById('exit-grid');
    const copyNotice = document.getElementById('copy-notice');

    const sampleMessages = [
        ...sampleTexts3.map(function (text) {
            return { author: 'Серёга Пират', text };
        }),
        ...sampleTexts.map(function (text) {
            return { author: 'qp_illson', text };
        }),
    ];

    function pickSampleMessage() {
        const buffer = new Uint32Array(1);
        crypto.getRandomValues(buffer);
        return sampleMessages[buffer[0] % sampleMessages.length];
    }

    function hashCodeToHue(text) {
        let hash = 0;
        for (let i = 0; i < text.length; i++) {
            hash = Math.imul(hash, 31) + text.codePointAt(i);
        }
        return Math.abs(hash) % 360;
    }

    const displayNameToLogin = {
        'qp_illson': 'qp_illson',
        'Серёга Пират': 'serega_pirat',
    };

    const avatarUrlCache = new Map();
    const pendingAvatarTargets = new Map();

    function applyAvatarImage(span, url) {
        const safe = String(url).replaceAll('"', String.raw`\"`);
        span.style.backgroundImage = 'url("' + safe + '")';
        span.style.backgroundSize = 'cover';
        span.style.backgroundPosition = 'center';
        span.style.color = 'transparent';
    }

    function ensureAvatarFetched(login) {
        if (avatarUrlCache.has(login)) {
            return;
        }
        avatarUrlCache.set(login, null);
        fetch('/api/user-avatar?login=' + encodeURIComponent(login))
            .then(function (response) {
                return response.ok ? response.json() : null;
            })
            .then(function (payload) {
                return payload?.url ? payload.url : null;
            })
            .catch(function () {
                return null;
            })
            .then(function (url) {
                avatarUrlCache.set(login, url || null);
                if (!url) return;
                const waiting = pendingAvatarTargets.get(login);
                if (!waiting) return;
                pendingAvatarTargets.delete(login);
                waiting.forEach(function (span) {
                    applyAvatarImage(span, url);
                });
            });
    }

    function buildFakeAvatar(author) {
        const span = document.createElement('span');
        span.className = 'avatar avatar-loaded demo-avatar-fallback';
        span.style.background = 'hsl(' + hashCodeToHue(author) + ', 55%, 45%)';
        span.textContent = (author || '?').trim().charAt(0).toUpperCase();

        const login = displayNameToLogin[author];
        if (login) {
            const cached = avatarUrlCache.get(login);
            if (typeof cached === 'string' && cached) {
                applyAvatarImage(span, cached);
            } else {
                if (!pendingAvatarTargets.has(login)) pendingAvatarTargets.set(login, []);
                pendingAvatarTargets.get(login).push(span);
                ensureAvatarFetched(login);
            }
        }

        return span;
    }

    function buildMessage(role) {
        const pick = pickSampleMessage();

        const div = document.createElement('div');
        div.className = 'message no-animation';

        if (role === 'first-time') {
            div.classList.add('first-time');
        } else if (role) {
            div.classList.add(role);
        }

        div.appendChild(buildFakeAvatar(pick.author));

        const content = document.createElement('div');
        content.className = 'message-content';

        const headerEl = document.createElement('div');
        headerEl.className = 'message-header';

        const timestamp = document.createElement('time');
        timestamp.className = 'timestamp';
        timestamp.textContent = new Date().toLocaleTimeString();
        headerEl.appendChild(timestamp);

        const username = document.createElement('span');
        username.className = role && role !== 'first-time'
            ? `username ${role}`
            : 'username';
        username.textContent = pick.author;
        headerEl.appendChild(username);

        content.appendChild(headerEl);

        const body = document.createElement('div');
        body.className = 'message-text';
        body.textContent = pick.text;
        content.appendChild(body);

        div.appendChild(content);
        return div;
    }

    function buildCard(animation, kind) {
        const card = document.createElement('article');
        card.className = 'demo-card';
        card.dataset.kind = kind;
        card.dataset.animation = animation.value;

        const header = document.createElement('header');
        header.className = 'demo-card-header';

        const label = document.createElement('span');
        label.className = 'demo-card-label';
        label.textContent = animation.label;
        header.appendChild(label);

        const value = document.createElement('code');
        value.className = 'demo-card-value';
        value.textContent = animation.value;
        value.title = 'Кликните, чтобы скопировать';
        value.addEventListener('click', function (event) {
            event.stopPropagation();
            copyToClipboard(animation.value);
        });
        header.appendChild(value);

        card.appendChild(header);

        const stage = document.createElement('div');
        stage.className = 'demo-stage';
        stage.appendChild(buildMessage(currentRole));
        card.appendChild(stage);

        card.addEventListener('click', function () {
            playCard(card);
        });

        return card;
    }

    function playCard(card) {
        const stage = card.querySelector('.demo-stage');
        const kind = card.dataset.kind;
        const animationValue = card.dataset.animation;

        const fresh = buildMessage(currentRole);
        stage.replaceChildren(fresh);

        // Принудительный reflow, чтобы перезапуск CSS-анимации гарантированно сработал.
        fresh.getBoundingClientRect();

        if (kind === 'entry') {
            if (animationValue === 'no-animation') {
                return;
            }
            fresh.classList.remove('no-animation');
            fresh.classList.add(animationValue);
        } else {
            fresh.classList.remove('no-animation');
            requestAnimationFrame(function () {
                fresh.dataset.exitAnim = animationValue;
            });
        }
    }

    function renderAll() {
        entryGrid.replaceChildren();
        for (const animation of entryAnimations) {
            entryGrid.appendChild(buildCard(animation, 'entry'));
        }

        exitGrid.replaceChildren();
        for (const animation of exitAnimations) {
            exitGrid.appendChild(buildCard(animation, 'exit'));
        }
    }

    function playAll() {
        const cards = document.querySelectorAll('.demo-card');
        let delay = 0;
        cards.forEach(function (card) {
            setTimeout(function () {
                playCard(card);
            }, delay);
            delay += 80;
        });
    }

    function copyToClipboard(text) {
        navigator.clipboard.writeText(text)
            .then(function () {
                showCopyNotice(text);
            })
            .catch(function (error) {
                console.warn('Не удалось скопировать имя анимации.', error);
            });
    }

    let copyNoticeTimer = null;

    function showCopyNotice(text) {
        copyNotice.textContent = 'Скопировано: ' + text;
        copyNotice.classList.add('visible');
        if (copyNoticeTimer !== null) {
            clearTimeout(copyNoticeTimer);
        }
        copyNoticeTimer = setTimeout(function () {
            copyNotice.classList.remove('visible');
        }, 1600);
    }

    document.querySelectorAll('.demo-role-button').forEach(function (button) {
        button.addEventListener('click', function () {
            currentRole = button.dataset.role || '';
            document.querySelectorAll('.demo-role-button').forEach(function (other) {
                other.classList.toggle('active', other === button);
            });
            renderAll();
        });
    });

    document.getElementById('play-all').addEventListener('click', playAll);

    const loopToggle = document.getElementById('loop-toggle');
    const loopIntervalSlider = document.getElementById('loop-interval');
    const loopIntervalValueLabel = document.getElementById('loop-interval-value');
    let loopIntervalMs = Number.parseInt(loopIntervalSlider.value, 10);
    let loopTimer = null;

    function formatSeconds(ms) {
        const seconds = ms / 1000;
        return ms % 1000 === 0
            ? seconds.toFixed(0)
            : seconds.toFixed(1);
    }

    function setLoopActive(active) {
        if (active) {
            if (loopTimer !== null) return;
            loopToggle.classList.add('active');
            loopToggle.setAttribute('aria-pressed', 'true');
            loopToggle.textContent = 'Цикл активен';
            playAll();
            loopTimer = setInterval(playAll, loopIntervalMs);
        } else {
            if (loopTimer === null) return;
            clearInterval(loopTimer);
            loopTimer = null;
            loopToggle.classList.remove('active');
            loopToggle.setAttribute('aria-pressed', 'false');
            loopToggle.textContent = 'Зациклить';
        }
    }

    loopToggle.addEventListener('click', function () {
        setLoopActive(loopTimer === null);
    });

    loopIntervalSlider.addEventListener('input', function () {
        loopIntervalMs = Number.parseInt(loopIntervalSlider.value, 10);
        loopIntervalValueLabel.textContent = formatSeconds(loopIntervalMs);
        if (loopTimer !== null) {
            clearInterval(loopTimer);
            loopTimer = setInterval(playAll, loopIntervalMs);
        }
    });

    loopIntervalValueLabel.textContent = formatSeconds(loopIntervalMs);

    function renderError(message) {
        const stub = function () {
            const wrapper = document.createElement('div');
            wrapper.className = 'demo-empty';
            wrapper.textContent = message;
            return wrapper;
        };
        entryGrid.replaceChildren(stub());
        exitGrid.replaceChildren(stub());
    }

    fetch('/api/animations')
        .then(response => {
            if (!response.ok) throw new Error('HTTP ' + response.status);
            return response.json();
        })
        .then(payload => {
            entryAnimations = payload.entry || [];
            exitAnimations = (payload.exit || []).filter(opt => opt.value !== 'no-animation');
            renderAll();
            setTimeout(playAll, 400);
        })
        .catch(error => {
            console.error('Не удалось загрузить /api/animations:', error);
            renderError('Не удалось загрузить список анимаций с сервера: ' + (error?.message ? error.message : error));
        });
}());

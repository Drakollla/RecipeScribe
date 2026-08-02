        let currentMode = 'youtube';
        let portions = 2;
        let activeRecipeId = null;
        let previousStateHtml = null;
        let recipeViewState = { id: null, portions: 1, ingredients: [], menuItemId: null };
        let menuViewState = {};
        let currentPlan = null;
        let goBackToMenu = false;


        // Инициализация при загрузке
        window.addEventListener('DOMContentLoaded', async () => {
            await loadUserSettings();
        });

        // ---- Отрисовка списка ингредиентов ----
        function buildIngredientItems(ingredients, flash) {
            var html = '';
            ingredients.forEach(function (i) {
                var cls = flash ? ' class="amount-flash"' : '';
                html += '<li>' +
                    '<span class="ingredient-link" data-recipe="' + recipeViewState.id + '" data-ingredient="' + i.name.replace(/"/g, '&quot;') + '" onclick="substituteIngredient(this)">' + i.name + '</span>' +
                    (i.amount ? ' — <strong' + cls + '>' + i.amount + '</strong>' : '') +
                    '</li>';
            });
            return html;
        }

        // Изменение целевого числа порций (без пересчёта — только число)
        function recipePortionsChange(delta) {
            if (!recipeViewState.id) return;
            var next = Math.max(1, Math.min(20, recipeViewState.portions + delta));
            if (next === recipeViewState.portions) return;

            recipeViewState.portions = next;

            var valEl = document.getElementById('recipePortionsVal');
            if (valEl) {
                valEl.innerText = next;
                valEl.classList.remove('value-bump');
                void valEl.offsetWidth;
                valEl.classList.add('value-bump');
            }
        }

        // Пересчёт ингредиентов через LLM (для яиц/специй и т.п.)
        async function llmRescale() {
            if (!recipeViewState.id) return;
            const itemId = recipeViewState.menuItemId;
            await showRecipe(recipeViewState.id, recipeViewState.portions, itemId);

            // Рецепт открыт из меню — отражаем пересчёт в карточке и в БД
            if (itemId && menuViewState[itemId]) {
                const st = menuViewState[itemId];
                st.portions = recipeViewState.portions;
                st.ingredients = recipeViewState.ingredients.map(function (x) {
                    return { name: x.name, amount: x.amount, originalName: x.originalName };
                });

                if (st.item) {
                    st.item.portions = st.portions;
                    st.item.ingredients = st.ingredients.slice();
                }
                patchMenuItem(itemId, st);
            }
        }

        function prepareRecipeView(recipe, portionsOverride, menuItemId) {
            recipeViewState.id = recipe.id;
            // На старте показываем ингредиенты как сохранены (оригинальные количества).
            // Число порций — целевое (дефолт из настроек или порции пункта меню).
            recipeViewState.portions = (portionsOverride != null) ? portionsOverride : portions;
            recipeViewState.ingredients = recipe.ingredients;
            recipeViewState.menuItemId = menuItemId || null;
        }

        // Загрузка настроек пользователя с сервера
        async function loadUserSettings() {
            try {
                const r = await fetch('/api/users/0/settings');
                if (r.ok) {
                    const data = await r.json();
                    _settingsCache = data;
                    if (data.defaultServings) {
                        portions = data.defaultServings;
                    }
                    updateObsidianStatus(data.obsidianVaultPath || '');
                }
            } catch (e) {
                // не критично
            }
        }

        function updateObsidianStatus(path) {
            const el = document.getElementById('obsidianStatus');
            el.innerText = path ? '✓' : 'не настроен';
            el.title = path || '';
        }

        // Настройка пути к Obsidian
        async function setObsidianPath() {
            switchMode('settings', document.getElementById('mode-settings'));
        }

        async function browseFolder() {
            openFolderBrowser(function (path) {
                document.getElementById('settingsObsidianPath').value = path;
            });
        }

        function openFolderBrowser(onSelect) {
            var modal = document.getElementById('folderBrowserModal');
            var currentPath = '';

            function render(path) {
                currentPath = path || '';
                fetch('/api/filesystem/directories?path=' + encodeURIComponent(currentPath))
                    .then(function (r) {
                        if (!r.ok) throw new Error('API not available');
                        return r.json();
                    })
                    .then(function (data) {
                        var html = '<div class="folder-modal-overlay" onclick="if(event.target===this)closeFolderBrowser()">' +
                            '<div class="folder-modal">' +
                            '<div class="folder-modal-header">Выберите папку</div>';

                        if (data.current) {
                            html += '<div class="folder-modal-breadcrumb">';
                            var parts = data.current.replace(/\\/g, '/').split('/');
                            var accumulated = '';
                            parts.forEach(function (part, i) {
                                if (i === 0) { accumulated = part; }
                                else { accumulated += '\\' + part; }
                                var p = accumulated;
                                html += '<span onclick="openFolderBrowser.__navigate(\'' + p.replace(/\\/g, '\\\\') + '\')">' + part + '/</span>';
                            });
                            html += '</div>';
                        }

                        html += '<div class="folder-modal-list">';
                        if (!data.current) {
                            data.dirs.forEach(function (d) {
                                html += '<div class="folder-modal-item" onclick="openFolderBrowser.__navigate(\'' + d.replace(/\\/g, '\\\\') + '\')">💻 ' + d + '</div>';
                            });
                        } else {
                            var parent = data.current.replace(/\\[^\\]+$/, '');
                            if (parent !== data.current) {
                                html += '<div class="folder-modal-item" onclick="openFolderBrowser.__navigate(\'' + parent.replace(/\\/g, '\\\\') + '\')">⬆️ ..</div>';
                            }
                            data.dirs.forEach(function (d) {
                                var full = currentPath + '\\' + d;
                                html += '<div class="folder-modal-item" onclick="openFolderBrowser.__navigate(\'' + full.replace(/\\/g, '\\\\') + '\')">📁 ' + d + '</div>';
                            });
                            if (data.dirs.length === 0 && !data.error) {
                                html += '<div style="padding:12px;color:var(--text-muted);">Папки отсутствуют</div>';
                            }
                            if (data.error) {
                                html += '<div style="padding:12px;color:var(--text-muted);">' + data.error + '</div>';
                            }
                        }
                        html += '</div>';

                        html += '<div class="folder-modal-footer">';
                        if (currentPath) {
                            html += '<button class="folder-select-btn" onclick="openFolderBrowser.__select()">✓ Выбрать</button>';
                        }
                        html += '<button onclick="closeFolderBrowser()">Отмена</button>';
                        html += '</div></div></div>';
                        modal.innerHTML = html;
                        modal.style.display = 'block';
                    })
                    .catch(function (e) {
                        alert('Не удалось загрузить проводник. Убедитесь, что сервер запущен.\n' + e.message);
                    });
            }

            openFolderBrowser.__navigate = function (p) { render(p); };
            openFolderBrowser.__select = function () {
                if (onSelect) onSelect(currentPath);
                closeFolderBrowser();
            };
            render('');
        }

        function closeFolderBrowser() {
            document.getElementById('folderBrowserModal').style.display = 'none';
            document.getElementById('folderBrowserModal').innerHTML = '';
        }

        let _settingsCache = {};

        async function renderSettingsForm() {
            showLoading();
            try {
                const r = await fetch('/api/users/0/settings');
                const data = r.ok ? await r.json() : {};
                _settingsCache = data;

                var html =
                    '<div class="settings-form">' +
                    '<h2 style="margin:0 0 8px 0;">Настройки</h2>' +
                    '<p style="color:var(--text-muted);margin-bottom:16px;">Настройки сохраняются в вашем профиле и используются во всех режимах.</p>' +

                    '<label>Порций по умолчанию</label>' +
                    '<input type="number" id="settingsServings" min="1" max="20" value="' + (data.defaultServings || 2) + '" style="width:100px;">' +

                    '<label>Путь к Obsidian Vault (папка с рецептами)</label>' +
                    '<div style="display:flex;gap:8px;align-items:center;">' +
                    '<input type="text" id="settingsObsidianPath" value="' + (data.obsidianVaultPath || '') + '" placeholder="D:\\обсидиан\\Заметки\\Заметки\\Рецепты" style="flex:1;">' +
                    '<button type="button" onclick="browseFolder()" style="padding:10px 14px;border:1px solid var(--border-color);border-radius:10px;background:var(--card-bg);color:var(--text-muted);font-size:14px;cursor:pointer;">📁 Обзор</button>' +
                    '</div>' +

                    '<br><button class="save-btn" onclick="saveSettings()">💾 Сохранить</button>' +
                    '<span id="settingsSaveMsg" style="margin-left:12px;"></span>' +

                    '<div id="llmProfilesSection" style="margin-top:28px;"></div>' +

                    '</div>';

                hideLoading();
                renderResults(html);
                loadLlmProfiles();
            } catch (e) {
                hideLoading();
                renderResults('<h2>Ошибка</h2><p>Не удалось загрузить настройки: ' + e.message + '</p>');
            }
        }

        async function saveSettings() {
            var servings = parseInt(document.getElementById('settingsServings').value) || 2;
            var obsidianPath = document.getElementById('settingsObsidianPath').value.trim();
            var msgEl = document.getElementById('settingsSaveMsg');

            showLoading();
            try {
                const r = await fetch('/api/users/0/settings', {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ defaultServings: servings, obsidianVaultPath: obsidianPath })
                });
                hideLoading();
                if (!r.ok) throw new Error((await r.json()).error || 'Ошибка сохранения');

                // обновляем глобальные переменные и кэш
                _settingsCache.defaultServings = servings;
                _settingsCache.obsidianVaultPath = obsidianPath;
                portions = servings;
                updateObsidianStatus(obsidianPath);

                msgEl.innerText = '✓ Сохранено';
                msgEl.style.color = '#4caf50';
                setTimeout(function () { msgEl.innerText = ''; }, 3000);
            } catch (e) {
                hideLoading();
                msgEl.innerText = '✗ ' + e.message;
                msgEl.style.color = '#f44336';
            }
        }

        // ===== LLM-профили (подключение модели через UI) =====
        async function loadLlmProfiles() {
            var section = document.getElementById('llmProfilesSection');
            if (!section) return;

            section.innerHTML = '<p style="color:var(--text-muted);font-size:13px;">Загрузка LLM-профилей...</p>';
            try {
                const r = await fetch('/api/llm/profiles');
                if (!r.ok) throw new Error((await r.json()).error || 'Ошибка');
                const data = await r.json();

                var active = data.active || {};
                var activeName = null;
                (data.profiles || []).forEach(function (p) {
                    if (p.endpoint === active.endpoint && p.modelId === active.modelId) {
                        activeName = p.name;
                    }
                });

                var html =
                    '<h3 style="margin:0 0 4px 0;">Подключение модели (LLM)</h3>' +
                    '<p style="color:var(--text-muted);margin:0 0 12px 0;font-size:13px;">Профили хранятся в папке tools. Активная модель применяется сразу, без перезапуска.</p>';

                html += '<div class="llm-profile-list">';
                (data.profiles || []).forEach(function (p) {
                    var isActive = p.name === activeName;
                    html += '<div class="llm-profile-item' + (isActive ? ' active' : '') + '">' +
                        '<div style="flex:1;min-width:0;">' +
                        '<div style="font-weight:600;">' + escapeHtml(p.name) + (isActive ? ' <span style="color:#4caf50;font-size:12px;">● активно</span>' : '') + '</div>' +
                        '<div style="color:var(--text-muted);font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + escapeHtml(p.modelId) + '</div>' +
                        '</div>' +
                        '<button class="llm-profile-btn" onclick="activateLlmProfile(\'' + escapeHtml(p.name).replace(/'/g, "\\'") + '\')"' + (isActive ? ' disabled' : '') + ' style="' + (isActive ? 'opacity:0.5;' : '') + '">Активировать</button>' +
                        '<button class="llm-profile-btn danger" onclick="deleteLlmProfile(\'' + escapeHtml(p.name).replace(/'/g, "\\'") + '\')">✕</button>' +
                        '</div>';
                });
                if (!(data.profiles || []).length) {
                    html += '<p style="color:var(--text-muted);font-size:13px;">Пока нет сохранённых профилей.</p>';
                }
                html += '</div>';

                html +=
                    '<div class="llm-profile-add" style="margin-top:12px;display:flex;flex-direction:column;gap:8px;border:1px solid var(--border-color);border-radius:12px;padding:12px;">' +
                    '<div style="font-weight:600;font-size:14px;">Добавить профиль</div>' +
                    '<input type="text" id="llmProfileName" placeholder="Название (например: Groq, Ollama)" style="width:100%;">' +
                    '<input type="text" id="llmProfileEndpoint" placeholder="Endpoint (например: https://api.groq.com/openai/v1)" style="width:100%;">' +
                    '<input type="text" id="llmProfileModel" placeholder="Model ID (например: openai/gpt-oss-120b)" style="width:100%;">' +
                    '<button class="save-btn" onclick="saveLlmProfile()" style="align-self:flex-start;">💾 Сохранить профиль</button>' +
                    '<span id="llmProfileMsg" style="font-size:13px;"></span>' +
                    '</div>';

                section.innerHTML = html;
            } catch (e) {
                section.innerHTML = '<p style="color:var(--text-muted);font-size:13px;">Не удалось загрузить LLM-профили: ' + escapeHtml(e.message) + '</p>';
            }
        }

        async function activateLlmProfile(name) {
            if (!confirm('Активировать профиль "' + name + '"?')) return;
            try {
                const r = await fetch('/api/llm/profiles/active', {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: name })
                });
                if (!r.ok) throw new Error((await r.json()).error || 'Ошибка');
                loadLlmProfiles();
            } catch (e) {
                alert('Ошибка: ' + e.message);
            }
        }

        async function saveLlmProfile() {
            var name = document.getElementById('llmProfileName').value.trim();
            var endpoint = document.getElementById('llmProfileEndpoint').value.trim();
            var model = document.getElementById('llmProfileModel').value.trim();
            var msgEl = document.getElementById('llmProfileMsg');

            if (!name || !endpoint || !model) {
                msgEl.innerText = '✗ Заполните все поля';
                msgEl.style.color = '#f44336';
                return;
            }

            try {
                const r = await fetch('/api/llm/profiles', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: name, endpoint: endpoint, modelId: model })
                });
                if (!r.ok) throw new Error((await r.json()).error || 'Ошибка');
                msgEl.innerText = '✓ Профиль сохранён';
                msgEl.style.color = '#4caf50';
                document.getElementById('llmProfileName').value = '';
                document.getElementById('llmProfileEndpoint').value = '';
                document.getElementById('llmProfileModel').value = '';
                setTimeout(function () { msgEl.innerText = ''; }, 3000);
                loadLlmProfiles();
            } catch (e) {
                msgEl.innerText = '✗ ' + e.message;
                msgEl.style.color = '#f44336';
            }
        }

        async function deleteLlmProfile(name) {
            if (!confirm('Удалить профиль "' + name + '"?')) return;
            try {
                const r = await fetch('/api/llm/profiles/' + encodeURIComponent(name), { method: 'DELETE' });
                if (!r.ok) throw new Error((await r.json()).error || 'Ошибка');
                loadLlmProfiles();
            } catch (e) {
                alert('Ошибка: ' + e.message);
            }
        }

        // Интерактивное переключение режимов
        async function switchMode(mode, element) {
            currentMode = mode;
            previousStateHtml = null; // Очищаем историю переходов при явном клике на другую вкладку
            goBackToMenu = false;

            const buttons = document.querySelectorAll('.mode-btn');
            buttons.forEach(btn => btn.classList.remove('active'));
            element.classList.add('active');

            const input = document.getElementById('mainInput');
            const icon = document.getElementById('inputIcon');
            const cmdBar = document.querySelector('.command-bar');
            const hint = document.querySelector('.hint-text');

            // Сброс полей ввода
            input.value = '';

            if (mode === 'settings') {
                cmdBar.style.display = 'none';
                hint.style.display = 'none';
                hideResults();
                await renderSettingsForm();
                return;
            }

            cmdBar.style.display = '';
            hint.style.display = '';

            if (mode === 'youtube') {
                input.placeholder = "Вставьте ссылку на кулинарное видео на YouTube...";
                icon.innerText = "🔗";
                hideResults();
            } else if (mode === 'products') {
                input.placeholder = "Введите ингредиенты через запятую (например: курица, грибы, сливки)...";
                icon.innerText = "🔍";
                hideResults();
            } else if (mode === 'menu') {
                input.placeholder = "Нажмите Enter или кнопку отправки для генерации меню...";;
                icon.innerText = "✏️";
                await loadCurrentMenu();
            } else if (mode === 'recipes') {
                input.placeholder = "Поиск среди сохраненных рецептов...";
                icon.innerText = "📂";
                await loadAllRecipes();
            }
        }

        // Показ лоадера
        function showLoading() {
            document.getElementById('loader').style.display = 'flex';
        }

        // Скрытие лоадера
        function hideLoading() {
            document.getElementById('loader').style.display = 'none';
        }

        // Скрытие результатов (не затрагивая сохраненную историю при переходах внутри потока)
        function hideResults() {
            document.getElementById('resultsContainer').style.display = 'none';
            activeRecipeId = null;
        }

        // Вывод HTML-результатов
        function renderResults(html) {
            const container = document.getElementById('resultsContainer');
            container.innerHTML = html;
            container.style.display = 'block';
        }

        // Возврат к предыдущему сохраненному экрану (из рецепта назад в меню/поиск)
        function goBack() {
            // Из рецепта возвращаемся в меню — перерисовываем его с обновлёнными порциями
            if (goBackToMenu && currentPlan) {
                goBackToMenu = false;
                activeRecipeId = null;
                previousStateHtml = null;
                renderResults(renderMealPlanHtml(currentPlan));
                return;
            }
            if (previousStateHtml) {
                const container = document.getElementById('resultsContainer');
                container.innerHTML = previousStateHtml;
                container.style.display = 'block';
                activeRecipeId = null;
                previousStateHtml = null;
            }
        }

        // Выполнение экшена при отправке
        async function executeAction() {
            const value = document.getElementById('mainInput').value.trim();

            if (currentMode === 'youtube') {
                if (!value) return;
                showLoading();
                hideResults();
                try {
                    const r = await fetch('/api/recipes/extract', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ url: value })
                    });
                    if (!r.ok) throw new Error((await r.json()).error || 'Не удалось обработать видео');
                    const recipes = await r.json();
                    hideLoading();
                    if (Array.isArray(recipes)) {
                        if (recipes.length === 1) {
                            const recipe = recipes[0];
                            activeRecipeId = recipe.id;
                            prepareRecipeView(recipe);
                            renderResults(renderRecipeHtml(recipe));
                        } else if (recipes.length > 1) {
                            let html = '<h2>Найдено рецептов: ' + recipes.length + '</h2><div class="recipe-list">';
                            recipes.forEach(r => {
                                html += '<div class="recipe-item" onclick="showRecipe(\'' + r.id + '\')">' +
                                    '<span class="recipe-item-title">' + r.title + '</span>' +
                                    '<span>➔</span></div>';
                            });
                            html += '</div>';
                            renderResults(html);
                        } else {
                            renderResults('<h2>Ошибка</h2><p>Не удалось извлечь рецепт.</p>');
                        }
                    } else {
                        // fallback: single object (backward compat)
                        activeRecipeId = recipes.id;
                        prepareRecipeView(recipes);
                        renderResults(renderRecipeHtml(recipes));
                    }
                } catch (e) {
                    hideLoading();
                    renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
                }
            }

            else if (currentMode === 'products') {
                if (!value) return;
                showLoading();
                hideResults();
                try {
                    const r = await fetch('/api/recipes/search?ingredients=' + encodeURIComponent(value));
                    const data = await r.json();
                    hideLoading();
                    if (data.length > 0) {
                        let html = '<h2>Найденные рецепты</h2><div class="recipe-list">';
                        data.forEach(recipe => {
                            html += `<div class="recipe-item" onclick="showRecipe('${recipe.id}')">
                                        <span class="recipe-item-title">${recipe.title}</span>
                                        <span>➔</span>
                                     </div>`;
                        });
                        html += '</div>';
                        renderResults(html);
                    } else {
                        renderResults('<h2>Результаты поиска</h2><p>Рецептов с такими ингредиентами не найдено.</p>');
                    }
                } catch (e) {
                    hideLoading();
                    renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
                }
            }

            else if (currentMode === 'menu') {
                showLoading();
                hideResults();
                try {
                    const r = await fetch('/api/mealplans/generate?chatId=0', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ date: '' })
                    });
                    if (!r.ok) throw new Error(await extractError(r));
                    const plan = await r.json();
                    hideLoading();
                    renderResults(renderMealPlanHtml(plan));
                } catch (e) {
                    hideLoading();
                    renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
                }
            }

            else if (currentMode === 'recipes') {
                showLoading();
                hideResults();
                try {
                    const r = await fetch('/api/recipes');
                    const data = await r.json();
                    hideLoading();
                    const q = value.toLowerCase();
                    const filtered = data.filter(recipe =>
                        recipe.title.toLowerCase().includes(q) ||
                        (recipe.ingredientNames || []).some(function (name) {
                            return name.toLowerCase().includes(q);
                        })
                    );
                    if (filtered.length > 0) {
                        let html = '<h2>Мои рецепты</h2><div class="recipe-list">';
                        filtered.forEach(recipe => {
                            html += '<div class="recipe-item">' +
                                '<span class="recipe-item-title" onclick="showRecipe(\'' + recipe.id + '\')">' + recipe.title + '</span>' +
                                '<div style="display:flex;align-items:center;gap:8px;">' +
                                '<span style="cursor:pointer;font-size:16px;color:var(--text-muted);" onclick="showRecipe(\'' + recipe.id + '\')">➔</span>' +
                                '<span style="cursor:pointer;font-size:16px;color:#f44336;opacity:0.6;" onclick="deleteRecipe(\'' + recipe.id + '\', \'' + recipe.title.replace(/'/g, "\\'") + '\')" title="Удалить">🗑️</span>' +
                                '</div>' +
                                '</div>';
                        });
                        html += '</div>';
                        renderResults(html);
                    } else {
                        renderResults('<h2>Мои рецепты</h2><p>Ничего не найдено.</p>');
                    }
                } catch (e) {
                    hideLoading();
                    renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
                }
            }
        }

        // Автоматическая загрузка текущего меню
        async function loadCurrentMenu() {
            showLoading();
            hideResults();
            try {
                const r = await fetch('/api/mealplans?chatId=0');
                if (r.status === 204) {
                    hideLoading();
                    renderResults('<h2>Меню на сегодня</h2><p>Меню на сегодня еще не спланировано. Введите пожелания в строке выше, чтобы составить меню.</p>');
                    return;
                }
                if (!r.ok) throw new Error('Not found');
                const plan = await r.json();
                hideLoading();
                renderResults(renderMealPlanHtml(plan));
            } catch (e) {
                hideLoading();
                renderResults('<h2>Меню на сегодня</h2><p>Меню на сегодня еще не спланировано. Введите пожелания в строке выше, чтобы составить меню.</p>');
            }
        }

        // Автоматическая загрузка сохраненных рецептов
        async function loadAllRecipes() {
            showLoading();
            hideResults();
            try {
                const r = await fetch('/api/recipes');
                const data = await r.json();
                hideLoading();
                if (data.length > 0) {
                    let html = '<h2>Сохраненные рецепты</h2><div class="recipe-list">';
                    data.forEach(recipe => {
                        html += '<div class="recipe-item">' +
                            '<span class="recipe-item-title" onclick="showRecipe(\'' + recipe.id + '\')">' + recipe.title + '</span>' +
                            '<div style="display:flex;align-items:center;gap:8px;">' +
                            '<span style="cursor:pointer;font-size:16px;color:var(--text-muted);" onclick="showRecipe(\'' + recipe.id + '\')">➔</span>' +
                            '<span style="cursor:pointer;font-size:16px;color:#f44336;opacity:0.6;" onclick="deleteRecipe(\'' + recipe.id + '\', \'' + recipe.title.replace(/'/g, "\\'") + '\')" title="Удалить">🗑️</span>' +
                            '</div>' +
                            '</div>';
                    });
                    html += '</div>';
                    renderResults(html);
                } else {
                    renderResults('<h2>Сохраненные рецепты</h2><p>В вашей книге рецептов пока пусто.</p>');
                }
            } catch (e) {
                hideLoading();
                renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
            }
        }

        // Отрисовка разметки рецепта
        function renderRecipeHtml(recipe) {
            let html = `<h2>${recipe.title}</h2>`;

            html += '<div class="recipe-ingredients-header">' +
                '<h3>Ингредиенты</h3>' +
                '<div class="recipe-portions-widget" title="Пересчитать количество ингредиентов">' +
                '<span>🍽️</span>' +
                '<button class="portion-btn" onclick="recipePortionsChange(-1)">−</button>' +
                '<span class="recipe-portions-value" id="recipePortionsVal">' + recipeViewState.portions + '</span>' +
                '<button class="portion-btn" onclick="recipePortionsChange(1)">+</button>' +
                '<button class="portion-btn recipe-recalc-btn" onclick="llmRescale()" title="Пересчитать ингредиенты">↻</button>' +
                '</div>' +
                '</div>';
            html += '<ul id="recipeIngredientsList">' + buildIngredientItems(recipeViewState.ingredients, false) + '</ul>';

            if (recipe.nutrition) {
                const n = recipe.nutrition;
                if (n.perServing || n.per100g || n.total) {
                    html += '<h3>Пищевая ценность</h3>';
                    const labels = ['ккал', 'белки, г', 'жиры, г', 'углеводы, г', 'клетчатка, г'];
                    const fields = ['calories', 'protein', 'fat', 'carbs', 'fiber'];
                    html += '<div class="nutrition-table">';
                    html += '<div class="nt-row nt-header"><span>Показатель</span>';
                    if (n.perServing) html += '<span>На порцию</span>';
                    if (n.per100g) html += '<span>На 100 г</span>';
                    if (n.total) html += '<span>Всё блюдо</span>';
                    html += '</div>';
                    for (let i = 0; i < labels.length; i++) {
                        let vals = [];
                        if (n.perServing) vals.push(n.perServing[fields[i]]);
                        if (n.per100g) vals.push(n.per100g[fields[i]]);
                        if (n.total) vals.push(n.total[fields[i]]);
                        let hasAny = vals.some(v => v != null);
                        if (!hasAny) continue;
                        html += '<div class="nt-row"><span>' + labels[i] + '</span>';
                        for (let j = 0; j < vals.length; j++) {
                            html += '<span>' + (vals[j] != null ? vals[j].toFixed(1) : '—') + '</span>';
                        }
                        html += '</div>';
                    }
                    html += '</div>';
                } else {
                    html += renderNutritionBlock('Пищевая ценность', n);
                }
            }

            if (recipe.preparationTips && recipe.preparationTips.length > 0) {
                html += '<h3>Советы по подготовке</h3><ul>';
                recipe.preparationTips.forEach(t => {
                    html += '<li><strong>' + t.ingredient + ':</strong> ' + t.tip + '</li>';
                });
                html += '</ul>';
            }

            html += '<h3>Инструкция по приготовлению</h3><ol>';
            recipe.steps.forEach(s => { html += '<li>' + s.description + '</li>'; });
            html += '</ol>';

            // Группа кнопок внизу
            html += `<div class="btn-group">
                        <button class="action-btn" onclick="exportToObsidian('${recipe.id}')">💾 Сохранить в Obsidian</button>`;

            // Если есть сохраненный предыдущий экран (меню или поиск по продуктам) — выводим кнопку Назад
            if (previousStateHtml) {
                html += `<button class="action-btn secondary" onclick="goBack()">🔙 Назад</button>`;
            } else {
                html += `<button class="action-btn secondary" onclick="switchMode('recipes', document.getElementById('mode-recipes'))">🗂️ Ко всем рецептам</button>`;
            }

            html += `</div>`;

            return html;
        }

        function renderNutritionBlock(title, v) {
            if (!v) return '';
            let html = '<h3>' + title + '</h3><div class="nutrition-grid">';
            if (v.calories != null) html += '<div class="nutrition-item"><div class="value">' + v.calories.toFixed(1) + '</div><div class="label">ккал</div></div>';
            if (v.protein != null) html += '<div class="nutrition-item"><div class="value">' + v.protein.toFixed(1) + '</div><div class="label">белки, г</div></div>';
            if (v.fat != null) html += '<div class="nutrition-item"><div class="value">' + v.fat.toFixed(1) + '</div><div class="label">жиры, г</div></div>';
            if (v.carbs != null) html += '<div class="nutrition-item"><div class="value">' + v.carbs.toFixed(1) + '</div><div class="label">углеводы, г</div></div>';
            if (v.fiber != null) html += '<div class="nutrition-item"><div class="value">' + v.fiber.toFixed(1) + '</div><div class="label">клетчатка, г</div></div>';
            html += '</div>';
            return html;
        }

        // Отрисовка плана питания с кнопкой перегенерации
        function renderMealPlanHtml(plan) {
            menuViewState = {};
            currentPlan = plan;
            let html = `<h2>Меню на сегодня (${plan.date})</h2><div style="margin-top:12px;">`;

            plan.items.forEach(i => {
                const itemId = i.id;
                const ingredients = i.ingredients || [];
                menuViewState[itemId] = {
                    planId: plan.id,
                    recipeId: i.recipe.id,
                    portions: i.portions,
                    ingredients: ingredients,
                    item: i
                };

                html += `<div class="meal-card" id="mealCard_${itemId}">
                            <div class="meal-card-header">
                                <span class="meal-type">${i.mealType}</span>
                                <span class="meal-recipe-btn" onclick="openMenuRecipe('${itemId}')">${i.recipe.title}</span>
                                <div class="meal-portions-widget" title="Пересчитать количество ингредиентов">
                                    <span>🍽️</span>
                                    <button class="portion-btn" onclick="menuPortionsChange('${itemId}', -1)">−</button>
                                    <span class="meal-portions-value" id="menuPortionsVal_${itemId}">${i.portions}</span>
                                    <button class="portion-btn" onclick="menuPortionsChange('${itemId}', 1)">+</button>
                                    <button class="portion-btn meal-recalc-btn" onclick="llmMenuRescale('${itemId}')" title="Пересчитать ингредиенты">↻</button>
                                </div>
                            </div>
                            <ul class="meal-ingredients" id="menuIngredients_${itemId}">${buildMenuIngredientItems(ingredients, itemId, i.recipe.id)}</ul>
                         </div>`;
            });

            html += '</div>';

            // Группа кнопок: список покупок и генерация нового меню
            html += `<div class="btn-group">
                        <button class="action-btn" onclick="showShoppingList('${plan.id}')">🛒 Список покупок</button>
                        <button class="action-btn secondary" onclick="regenerateMenu()">🔄 Сгенерировать заново</button>
                     </div>`;
            return html;
        }

        // Отрисовка ингредиентов пункта меню (с кликом для замены через LLM)
        function buildMenuIngredientItems(ingredients, itemId, recipeId) {
            var html = '';
            ingredients.forEach(function (i) {
                html += '<li>' +
                    '<span class="ingredient-link" data-recipe="' + recipeId + '" data-ingredient="' + i.name.replace(/"/g, '&quot;') + '" onclick="substituteMenuIngredient(this, \'' + itemId + '\')">' + i.name + '</span>' +
                    (i.amount ? ' — <strong>' + i.amount + '</strong>' : '') +
                    '</li>';
            });
            return html;
        }

        // Изменение целевого числа порций пункта меню (без пересчёта — только число)
        function menuPortionsChange(itemId, delta) {
            var st = menuViewState[itemId];
            if (!st || st.busy) return;
            var next = Math.max(1, Math.min(20, st.portions + delta));
            if (next === st.portions) return;

            st.portions = next;

            var valEl = document.getElementById('menuPortionsVal_' + itemId);
            if (valEl) {
                valEl.innerText = next;
                valEl.classList.remove('value-bump');
                void valEl.offsetWidth;
                valEl.classList.add('value-bump');
            }
        }

        // Пересчёт ингредиентов пункта меню через LLM + сохранение порций в БД
        async function llmMenuRescale(itemId) {
            var st = menuViewState[itemId];
            if (!st || st.busy) return;

            // Мьютим карточку и показываем спиннер на время LLM-запроса
            st.busy = true;
            var card = document.getElementById('mealCard_' + itemId);
            if (card) card.classList.add('is-busy');
            showLoading();

            try {
                const r = await fetch(`/api/recipes/${st.recipeId}?servings=${st.portions}&itemId=${itemId}`);
                if (!r.ok) throw new Error('Не удалось пересчитать');
                const recipe = await r.json();

                st.ingredients = (recipe.ingredients || []).map(function (x) {
                    return { name: x.name, amount: x.amount, originalName: x.originalName };
                });
                st.portions = recipe.servings;

                if (st.item) {
                    st.item.portions = st.portions;
                    st.item.ingredients = st.ingredients.slice();
                }

                var valEl = document.getElementById('menuPortionsVal_' + itemId);
                if (valEl) valEl.innerText = st.portions;

                var list = document.getElementById('menuIngredients_' + itemId);
                if (list) list.innerHTML = buildMenuIngredientItems(st.ingredients, itemId, st.recipeId);

                patchMenuItem(itemId, st);
            } catch (e) {
                alert(e.message);
            } finally {
                st.busy = false;
                if (card) card.classList.remove('is-busy');
                hideLoading();
            }
        }

        // Открытие рецепта из меню — сразу с порциями пункта
        function openMenuRecipe(itemId) {
            var st = menuViewState[itemId];
            if (!st) return;
            goBackToMenu = true;
            showRecipe(st.recipeId, st.portions, itemId);
        }

        // Удаление рецепта
        async function deleteRecipe(id, title) {
            if (!confirm('Удалить рецепт "' + title + '"? Это действие необратимо.')) return;
            showLoading();
            try {
                const r = await fetch('/api/recipes/' + id, { method: 'DELETE' });
                hideLoading();
                if (!r.ok) throw new Error('Не удалось удалить рецепт');
                if (currentMode === 'recipes') loadAllRecipes();
            } catch (e) {
                hideLoading();
                renderResults('<h2>Ошибка</h2><p>' + e.message + '</p>');
            }
        }

        async function showRecipe(id, servingsOverride, menuItemId) {
            showLoading();

            if (activeRecipeId === null) {
                previousStateHtml = document.getElementById('resultsContainer').innerHTML;
            }

            hideResults();
            activeRecipeId = id;
            try {
                const url = servingsOverride != null
                    ? `/api/recipes/${id}?servings=${servingsOverride}${menuItemId ? '&itemId=' + menuItemId : ''}`
                    : `/api/recipes/${id}${menuItemId ? '?itemId=' + menuItemId : ''}`;
                const r = await fetch(url);
                if (!r.ok) throw new Error('Рецепт не найден');
                const recipe = await r.json();

                prepareRecipeView(recipe, servingsOverride, menuItemId);

                hideLoading();
                renderResults(renderRecipeHtml(recipe));
            } catch (e) {
                hideLoading();
                renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
            }
        }

        // Экспорт в Obsidian
        function isAbsolutePath(p) {
            return /^[A-Za-z]:\\/.test(p);
        }

        async function exportToObsidian(id) {
            try {
                const r = await fetch(`/api/recipes/${id}/export-to-obsidian?chatId=0`, { method: 'POST' });
                const data = await r.json();
                if (r.ok) {
                    alert('Рецепт успешно сохранён в Obsidian!\nПуть: ' + data.path);
                } else {
                    alert('Ошибка: ' + (data.error || 'Не удалось сохранить'));
                }
            } catch (e) {
                alert('Ошибка экспорта: ' + e.message);
            }
        }

        // Список покупок
        async function showShoppingList(planId) {
            showLoading();
            setMenuLocked(true);
            try {
                const r = await fetch(`/api/mealplans/${planId}/shopping-list`);
                if (!r.ok) throw new Error('Не удалось загрузить список покупок');
                const text = await r.text();
                hideLoading();
                setMenuLocked(false);

                // Конвертируем markdown в HTML (звёздочки → жирный, • → маркеры)
                var html = text
                    .replace(/\*(.*?)\*/g, '<b>$1</b>')
                    .replace(/^• /gm, '&bull; ')
                    .replace(/\n/g, '<br>');

                renderResults(`
                    <h2>Список покупок</h2>
                    <div style="line-height: 1.8; color: var(--text-main); font-size: 15px;">${html}</div>
                    <div class="btn-group">
                        <button class="action-btn secondary" onclick="loadCurrentMenu()">📅 Назад к меню</button>
                    </div>
                `);
            } catch (e) {
                hideLoading();
                setMenuLocked(false);
                alert('Ошибка: ' + e.message);
            }
        }

        // Блокировка изменения карточек меню, пока собирается список покупок
        function setMenuLocked(locked) {
            document.querySelectorAll('.meal-card').forEach(function (card) {
                if (locked) card.classList.add('is-busy');
                else card.classList.remove('is-busy');
            });
        }

        // Перегенерация меню (запрос нового плана)
        async function regenerateMenu() {
            showLoading();
            hideResults();
            try {
                const r = await fetch('/api/mealplans/generate?chatId=0', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ date: '' })
                });
                if (!r.ok) throw new Error(await extractError(r));
                const plan = await r.json();
                hideLoading();
                renderResults(renderMealPlanHtml(plan));
            } catch (e) {
                hideLoading();
                renderResults(`<h2>Ошибка генерации</h2><p>${e.message}</p>`);
            }
        }

        // ===== Замена ингредиента (поповер) =====
        let substituteRecipeId = null;
        let substituteIngredientEl = null;
        let substituteMenuItemId = null; // id пункта меню, если замена делается на карточке (иначе null — страница рецепта)

        function substituteIngredient(el) {
            openSubstitutePopover(el, null);
        }

        function substituteMenuIngredient(el, itemId) {
            openSubstitutePopover(el, itemId);
        }

        function openSubstitutePopover(el, itemId) {
            substituteRecipeId = el.dataset.recipe;
            substituteIngredientEl = el;
            substituteMenuItemId = itemId;
            const ingredientName = el.dataset.ingredient;

            // Создаём overlay + popover
            const overlay = document.createElement('div');
            overlay.className = 'substitute-overlay';
            overlay.id = 'substituteOverlay';
            overlay.onclick = function (e) { if (e.target === overlay) closeSubstitutePopover(); };

            overlay.innerHTML = '<div class="substitute-popover" onclick="event.stopPropagation()">' +
                '<button class="close-btn" onclick="closeSubstitutePopover()">✕</button>' +
                '<h3>Замена: ' + escapeHtml(ingredientName) + '</h3>' +
                '<p class="subtitle">Подбираю варианты...</p>' +
                '<div id="substituteBody">' +
                '<div class="skeleton-item"></div>' +
                '<div class="skeleton-item"></div>' +
                '<div class="skeleton-item"></div>' +
                '</div>' +
                '</div>';

            document.body.appendChild(overlay);

            // Запрос к API
            fetch('/api/recipes/' + substituteRecipeId + '/substitute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ingredient: ingredientName })
            })
                .then(function (r) {
                    if (!r.ok) throw new Error('Ошибка');
                    return r.json();
                })
                .then(function (data) {
                    renderSubstituteSuggestions(data.suggestions || []);
                })
                .catch(function () {
                    document.getElementById('substituteBody').innerHTML = '<p style="color:var(--text-muted);padding:12px 0;">Не удалось подобрать замену.</p>';
                });
        }

        function renderSubstituteSuggestions(suggestions) {
            var body = document.getElementById('substituteBody');
            if (!suggestions.length) {
                body.innerHTML = '<p style="color:var(--text-muted);padding:12px 0;">Нет вариантов замены.</p>';
                return;
            }

            var html = '';
            suggestions.forEach(function (s) {
                html += '<div class="suggestion-card" data-name="' + encodeURIComponent(s.name) + '" onclick="selectSubstitute(this)">' +
                    '<div class="name">' + escapeHtml(s.name) + '</div>' +
                    '<div class="desc">' + escapeHtml(s.description || '') + '</div>' +
                    '</div>';
            });

            html += '<div class="custom-variant">' +
                '<input type="text" id="customSubstituteInput" placeholder="Свой вариант..." onkeydown="if(event.key===\'Enter\') selectCustomSubstitute()">' +
                '<button onclick="selectCustomSubstitute()">OK</button>' +
                '</div>';

            body.innerHTML = html;
        }

        function selectSubstitute(el) {
            applySubstitution(decodeURIComponent(el.dataset.name));
        }

        function selectCustomSubstitute() {
            var input = document.getElementById('customSubstituteInput');
            var val = input.value.trim();
            if (val) applySubstitution(val);
        }

        function applySubstitution(newName) {
            if (!substituteIngredientEl) return;

            var originalName = substituteIngredientEl.dataset.ingredient;

            // Заменяем текст
            substituteIngredientEl.innerText = newName;
            substituteIngredientEl.dataset.ingredient = newName;

            // Вспышка
            substituteIngredientEl.classList.remove('flash-highlight');
            void substituteIngredientEl.offsetWidth;
            substituteIngredientEl.classList.add('flash-highlight');

            setTimeout(function () {
                substituteIngredientEl.classList.remove('flash-highlight');
            }, 600);

            // Настоящее имя из рецепта, если текущее имя — уже применённая замена
            var originalFromRecipe = originalName;
            if (!substituteMenuItemId && recipeViewState.ingredients) {
                (recipeViewState.ingredients || []).forEach(function (ri) {
                    if (ri.name === originalName && ri.originalName) originalFromRecipe = ri.originalName;
                });
            }

            // Синхронизируем состояние страницы рецепта, чтобы при повторных заменах
            // сохранялся исходный originalName (настоящее имя из рецепта)
            if (!substituteMenuItemId && recipeViewState.ingredients) {
                recipeViewState.ingredients.forEach(function (ri) {
                    if (ri.name === originalName) {
                        if (!ri.originalName) ri.originalName = originalFromRecipe;
                        ri.name = newName;
                    }
                });
            }

            // Замена на карточке меню ИЛИ на странице рецепта, открытого из меню, —
            // сохраняем в пункт меню (IngredientsJson), чтобы пережить перезапуск и пересчёт порций.
            var targetItemId = substituteMenuItemId || recipeViewState.menuItemId;
            if (targetItemId) {
                var st = menuViewState[targetItemId];
                if (st) {
                    st.ingredients.forEach(function (ing) {
                        if (ing.name === originalName || ing.originalName === originalName) {
                            if (!ing.originalName) ing.originalName = originalFromRecipe;
                            ing.name = newName;
                        }
                    });
                    if (st.item) st.item.ingredients = st.ingredients.slice();
                    patchMenuItem(targetItemId, st);
                }
            }

            closeSubstitutePopover();
        }

        // Сохранение порций и ингредиентов пункта меню в БД
        function patchMenuItem(itemId, st) {
            fetch(`/api/mealplans/items/${itemId}`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ portions: st.portions, ingredients: st.ingredients })
            }).catch(function (e) { });
        }

        function closeSubstitutePopover() {
            var overlay = document.getElementById('substituteOverlay');
            if (overlay) {
                overlay.style.opacity = '0';
                setTimeout(function () {
                    if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
                }, 200);
            }
            substituteRecipeId = null;
            substituteIngredientEl = null;
            substituteMenuItemId = null;
        }

        // Безопасное извлечение ошибки из ответа сервера
        async function extractError(r) {
            if (r.status === 429) return 'Слишком много запросов. Попробуйте через 30 секунд.';
            try { return (await r.text()) || r.statusText; }
            catch { return r.statusText; }
        }

        function clearInput() {
            document.getElementById('mainInput').value = '';
            if (currentMode === 'recipes') loadAllRecipes();
            document.getElementById('mainInput').focus();
        }

        function escapeHtml(str) {
            var div = document.createElement('div');
            div.appendChild(document.createTextNode(str));
            return div.innerHTML;
        }

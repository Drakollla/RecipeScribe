        let currentMode = 'youtube';
        let portions = 2;
        let activeRecipeId = null;
        let previousStateHtml = null;
        // Что именно лежит в previousStateHtml: 'recipes' — вкладка «Мои рецепты» (её дешевле
        // перерисовать из свежего /api/recipes, чем отдавать устаревший снапшот), иначе null
        let previousStateKind = null;
        let recipeViewState = { id: null, recipe: null, portions: 1, ingredients: [], steps: [], variants: [], sourceRecipe: null, sourceId: null, variantTitle: null, menuItemId: null, modified: false };
        let currentRecipeObj = null;
        let menuViewState = {};
        let currentPlan = null;
        let goBackToMenu = false;
        // Вкладка «Мои рецепты»: кэш списка + фильтр приёма пищи + текстовый запрос
        let allRecipesCache = null;
        let recipesFilter = 'all';
        let recipesQuery = '';
        // Форма создания/редактирования: null — закрыта, иначе id рецепта (edit) либо '' (create)
        let recipeFormId = null;
        let recipeFormFrom = null; // 'list' | 'recipe' — куда вернуться по «Отмена»
        let recipeFormSaving = false;
        // Отслеживание правок в форме: «Сохранить» активна только при отличии от префилла
        let recipeFormDirty = false;
        let recipeFormBaseline = '';


        // Инициализация при загрузке
        window.addEventListener('DOMContentLoaded', async () => {
            await loadUserSettings();
        });

        // ---- Отрисовка списка ингредиентов ----
        function buildIngredientItems(ingredients, flash) {
            var html = '';
            ingredients.forEach(function (i) {
                var cls = flash ? ' class="amount-flash"' : '';
                var name = escapeHtml(i.name);
                html += '<li>' +
                    '<span class="ingredient-link" data-recipe="' + recipeViewState.id + '" data-ingredient="' + name + '" onclick="substituteIngredient(this)">' + name + '</span>' +
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
            await showRecipe(recipeViewState.id, recipeViewState.portions);
        }

        // Переключение рецепта в меню на вариант той же группы (PATCH item recipeId)
        async function switchMenuRecipeVariant(itemId, recipeId) {
            var st = menuViewState[itemId];
            if (!st || st.busy) return;

            st.busy = true;
            var card = document.getElementById('mealCard_' + itemId);
            if (card) card.classList.add('is-busy');
            showLoading();

            try {
                const r = await fetch('/api/mealplans/items/' + itemId, {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ portions: st.portions, recipeId: recipeId })
                });
                if (!r.ok) throw new Error('Не удалось сменить вариант');
                const item = await r.json();

                st.recipeId = item.recipe.id;
                st.ingredients = item.ingredients || [];
                st.substitutions = [];
                st.modified = false;
                if (st.item) {
                    st.item.recipe = item.recipe;
                    st.item.ingredients = st.ingredients.slice();
                }

                rerenderMealCard(itemId);
            } catch (e) {
                alert(e.message);
            } finally {
                st.busy = false;
                if (card) card.classList.remove('is-busy');
                hideLoading();
            }
        }

function prepareRecipeView(recipe, portionsOverride, menuItemId) {
            recipeViewState.id = recipe.id;
            recipeViewState.recipe = recipe;
            // На старте показываем ингредиенты как сохранены (оригинальные количества).
            // Число порций — целевое (default Recipes из порции пункта меню).
            recipeViewState.portions = (portionsOverride != null) ? portionsOverride : portions;
            recipeViewState.ingredients = recipe.ingredients;
            recipeViewState.steps = recipe.steps;
            recipeViewState.variants = recipe.variants || [];
            recipeViewState.sourceRecipe = recipe.sourceRecipe || null;
            recipeViewState.sourceId = recipe.sourceId || null;
            recipeViewState.variantTitle = recipe.variantTitle || null;
            recipeViewState.menuItemId = menuItemId || null;
            recipeViewState.modified = false;
            recipeViewState.substitutions = [];
            recipeViewState.savingVariant = false;
        }

        // Переключение на редакцию рецепта по чипу
        function showRecipeVariant(id) {
            if (id === recipeViewState.id) return;
            showRecipe(id);
        }

        // Сохранение текущего (возможно изменённого) рецепта как отдельного варианта
        async function saveAsVariant() {
            if (!recipeViewState.id || recipeViewState.savingVariant) return;

            var btn = document.querySelector('.recipe-modified-banner .action-btn');
            if (btn) btn.disabled = true;

            var title = prompt('Название варианта (например: "без сметаны"):');
            if (title === null) {
                if (btn) btn.disabled = false;
                return;
            }

            recipeViewState.savingVariant = true;
            var ingredients = (recipeViewState.ingredients || []).map(function (i) {
                return { name: i.name, amount: i.amount || '' };
            });

            showLoading();
            try {
                var steps = recipeViewState.steps || [];
                var tips = (recipeViewState.recipe && recipeViewState.recipe.preparationTips) || null;
                if (recipeViewState.substitutions && recipeViewState.substitutions.length) {
                    var rewritten = await batchRewriteSteps(recipeViewState.id, recipeViewState.substitutions);
                    if (rewritten) {
                        if (rewritten.steps.length) steps = rewritten.steps;
                        if (rewritten.tips) tips = rewritten.tips;
                    }
                }

                var stepsForVariant = steps.map(function (s) { return { number: s.number, description: s.description }; });

                const r = await fetch('/api/recipes/' + recipeViewState.id + '/variants', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ variantTitle: title, ingredients: ingredients, steps: stepsForVariant, preparationTips: tips })
                });
                if (!r.ok) throw new Error('Не удалось сохранить вариант');
                const created = await r.json();
                showRecipe(created.id);
            } catch (e) {
                alert('Ошибка: ' + e.message);
            } finally {
                recipeViewState.savingVariant = false;
                if (btn) btn.disabled = false;
                hideLoading();
            }
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

        let _settingsTab = 'general';

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

                    '<div class="settings-tabs">' +
                    '<button class="settings-tab-btn' + (_settingsTab === 'general' ? ' active' : '') + '" onclick="switchSettingsTab(\'general\')">Общие</button>' +
                    '<button class="settings-tab-btn' + (_settingsTab === 'llm' ? ' active' : '') + '" onclick="switchSettingsTab(\'llm\')">Подключение модели (LLM)</button>' +
                    '</div>' +

                    '<div id="settingsGeneralTab" class="settings-tab-pane"' + (_settingsTab !== 'general' ? ' style="display:none;"' : '') + '>' +
                    '<label>Порций по умолчанию</label>' +
                    '<input type="number" id="settingsServings" min="1" max="20" value="' + (data.defaultServings || 2) + '" style="width:100px;">' +

                    '<label>Путь к Obsidian Vault (папка с рецептами)</label>' +
                    '<div style="display:flex;gap:8px;align-items:center;">' +
                    '<input type="text" id="settingsObsidianPath" value="' + (data.obsidianVaultPath || '') + '" placeholder="D:\\обсидиан\\Заметки\\Заметки\\Рецепты" style="flex:1;">' +
                    '<button type="button" onclick="browseFolder()" style="padding:10px 14px;border:1px solid var(--border-color);border-radius:10px;background:var(--card-bg);color:var(--text-muted);font-size:14px;cursor:pointer;">📁 Обзор</button>' +
                    '</div>' +

                    '<br><button class="save-btn" onclick="saveSettings()">💾 Сохранить</button>' +
                    '<span id="settingsSaveMsg" style="margin-left:12px;"></span>' +
                    '</div>' +

                    '<div id="settingsLlmTab" class="settings-tab-pane"' + (_settingsTab !== 'llm' ? ' style="display:none;"' : '') + '>' +
                    '<div id="llmProfilesSection"></div>' +
                    '</div>' +

                    '</div>';

                hideLoading();
                renderResults(html);
                if (_settingsTab === 'llm') loadLlmProfiles();
            } catch (e) {
                hideLoading();
                renderResults('<h2>Ошибка</h2><p>Не удалось загрузить настройки: ' + e.message + '</p>');
            }
        }

        function switchSettingsTab(tab) {
            _settingsTab = tab;
            var generalEl = document.getElementById('settingsGeneralTab');
            var llmEl = document.getElementById('settingsLlmTab');
            var btnGeneral = document.querySelector('.settings-tab-btn[onclick*="\'general\'"]');
            var btnLlm = document.querySelector('.settings-tab-btn[onclick*="\'llm\'"]');
            if (generalEl) generalEl.style.display = tab === 'general' ? '' : 'none';
            if (llmEl) llmEl.style.display = tab === 'llm' ? '' : 'none';
            if (btnGeneral) btnGeneral.classList.toggle('active', tab === 'general');
            if (btnLlm) btnLlm.classList.toggle('active', tab === 'llm');
            if (tab === 'llm') loadLlmProfiles();
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
        // Оптимистичное переключение активного LLM-профиля, пока конфиг не перечитан
        var llmActiveOverride = null;

        async function loadLlmProfiles(silent) {
            var section = document.getElementById('llmProfilesSection');
            if (!section) return;

            if (!silent) section.innerHTML = '<p style="color:var(--text-muted);font-size:13px;">Загрузка LLM-профилей...</p>';
        try {
            const r = await fetch('/api/llm/profiles');
            if (!r.ok) throw new Error((await r.json()).error || 'Ошибка');
            const data = await r.json();

            var active = data.active || {};
            var activeName = null;
            if (llmActiveOverride) {
                (data.profiles || []).forEach(function (p) {
                    if (p.name === llmActiveOverride) activeName = p.name;
                });
            }
            if (!activeName) {
                (data.profiles || []).forEach(function (p) {
                    if (p.endpoint === active.endpoint && p.modelId === active.modelId) {
                        activeName = p.name;
                    }
                });
            }

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
            try {
                const r = await fetch('/api/llm/profiles/active', {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: name })
                });
                if (!r.ok) throw new Error((await r.json()).error || 'Ошибка');
                llmActiveOverride = name;
                loadLlmProfiles(true);
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
                loadLlmProfiles(true);
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
                loadLlmProfiles(true);
            } catch (e) {
                alert('Ошибка: ' + e.message);
            }
        }

        // Интерактивное переключение режимов
        async function switchMode(mode, element) {
            currentMode = mode;
            previousStateHtml = null; // Очищаем историю переходов при явном клике на другую вкладку
            previousStateKind = null;
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

        // ---- Фоновая загрузка рецептов: плашка как в Google Drive (не блокирует страницу) ----
        var JOB_PENDING = 0, JOB_PROCESSING = 1, JOB_COMPLETED = 2, JOB_FAILED = 3, JOB_CANCELLED = 4;
        var extractionRows = {};
        var extractionTimer = null;

        // Начать извлечение: POST возвращает 202 + jobId, сайт остаётся кликабельным
        async function enqueueExtraction(url) {
            var r = await fetch('/api/recipes/extract', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url: url })
            });
            if (r.status === 202) {
                var job = await r.json();
                addExtractionRow(job);
                return job;
            }
            if (r.status === 503)
                throw new Error('Очередь извлечения переполнена. Подождите и попробуйте снова.');
            throw new Error((await extractError(r)) || 'Не удалось поставить видео в очередь');
        }

        // Добавить строку в плашку и начать опрос статуса
        function addExtractionRow(job) {
            var list = document.getElementById('uploadTrayList');
            document.getElementById('uploadTray').style.display = '';

            var row = document.createElement('div');
            row.className = 'upload-item';
            row.setAttribute('data-jobid', job.id);
            row.innerHTML =
                '<div class="upload-icon"><span class="upload-spinner"></span></div>' +
                '<div class="upload-body">' +
                    '<div class="upload-title">' + escapeHtml(shortUrl(job.url)) + '</div>' +
                    '<div class="upload-status">' + escapeHtml(job.progressMessage || 'В очереди...') + '</div>' +
                '</div>' +
                '<div class="upload-actions"></div>';

            list.insertBefore(row, list.firstChild);
            extractionRows[job.id] = { url: job.url, recipeIds: [], busy: false };
            renderActionButtons(row, job);

            // Пытаемся подтянуть название видео из oEmbed (молча, если не выйдет)
            fetchVideoTitle(job.url).then(function (title) {
                if (title) {
                    var t = row.querySelector('.upload-title');
                    if (t) t.textContent = title;
                }
            });

            startExtractionPolling();
        }

        function rowOf(id) {
            return document.querySelector('.upload-item[data-jobid="' + id + '"]');
        }

        // Кнопки в строке плашки: отмена для активных, возобновление для отменённых, ✕ всегда
        function renderActionButtons(row, job) {
            var cell = row.querySelector('.upload-actions');
            if (!cell) return;

            var html = '';
            if (job.status === JOB_PENDING || job.status === JOB_PROCESSING) {
                html += '<button class="upload-btn" title="Отмена" onclick="cancelExtraction(\'' + job.id + '\')">⏹</button>';
            } else if (job.status === JOB_CANCELLED) {
                html += '<button class="upload-btn" title="Возобновить" onclick="resumeExtraction(\'' + job.id + '\')">▶</button>';
            }
            html += '<button class="upload-close" title="Закрыть" onclick="removeExtractionRow(\'' + job.id + '\')">✕</button>';
            cell.innerHTML = html;
        }

        // Отмена: POST /cancel, затем сразу перерисовываем строку по ответу сервера
        async function cancelExtraction(id) {
            var e = extractionRows[id];
            if (!e || e.busy) return;
            e.busy = true;
            try {
                var r = await fetch('/api/recipes/extract/jobs/' + id + '/cancel', { method: 'POST' });
                if (r.status === 404) {
                    removeExtractionRow(id);
                    return;
                }
                if (!r.ok) throw new Error(await extractError(r));
                renderExtractionRow(rowOf(id), await r.json());
            } catch (err) {
                var row = rowOf(id);
                if (row) {
                    var st = row.querySelector('.upload-status');
                    if (st) st.innerHTML = escapeHtml(err.message || 'Не удалось отменить задачу');
                }
            } finally {
                if (e) e.busy = false;
            }
        }

        // Возобновление: POST /resume — задача возвращается в очередь, опрос перезапускается
        async function resumeExtraction(id) {
            var e = extractionRows[id];
            if (!e || e.busy) return;
            e.busy = true;
            try {
                var r = await fetch('/api/recipes/extract/jobs/' + id + '/resume', { method: 'POST' });
                if (r.status === 404) {
                    removeExtractionRow(id);
                    return;
                }
                if (!r.ok) throw new Error(await extractError(r));
                renderExtractionRow(rowOf(id), await r.json());
                startExtractionPolling();
            } catch (err) {
                var row = rowOf(id);
                if (row) {
                    var st = row.querySelector('.upload-status');
                    if (st) st.innerHTML = escapeHtml(err.message || 'Не удалось возобновить задачу');
                }
            } finally {
                if (e) e.busy = false;
            }
        }

        function removeExtractionRow(id) {
            var row = document.querySelector('.upload-item[data-jobid="' + id + '"]');
            if (row && row.parentNode) row.parentNode.removeChild(row);
            delete extractionRows[id];
            if (!document.querySelectorAll('.upload-item').length)
                document.getElementById('uploadTray').style.display = 'none';
        }

        // Неблокирующий опрос статусов всех активных строк
        function startExtractionPolling() {
            if (extractionTimer) return;
            extractionTimer = setInterval(function () {
                if (!document.querySelectorAll('.upload-item:not(.is-finished)').length) {
                    clearInterval(extractionTimer);
                    extractionTimer = null;
                    return;
                }
                document.querySelectorAll('.upload-item:not(.is-finished)').forEach(function (row) {
                    pollExtractionRow(row);
                });
            }, 2000);
        }

        async function pollExtractionRow(row) {
            var id = row.getAttribute('data-jobid');
            try {
                var r = await fetch('/api/recipes/extract/jobs/' + id);
                if (!r.ok) throw new Error('Не удалось получить статус');
                renderExtractionRow(row, await r.json());
            } catch (e) {
                renderExtractionRow(row, { status: JOB_FAILED, id: id, error: e.message });
            }
        }

        function renderExtractionRow(row, job) {
            if (!row) return;
            var icon = row.querySelector('.upload-icon');
            var status = row.querySelector('.upload-status');

            // Активные состояния: спиннер, строка снова «живая»
            if (job.status === JOB_PENDING || job.status === JOB_PROCESSING) {
                row.classList.remove('is-finished');
                icon.innerHTML = '<span class="upload-spinner"></span>';
                status.innerHTML = escapeHtml(job.progressMessage || 'В очереди...');
                renderActionButtons(row, job);
                return;
            }

            // Завершённые состояния: строка больше не опрашивается
            row.classList.add('is-finished');

            if (job.status === JOB_COMPLETED) {
                if (extractionRows[job.id]) extractionRows[job.id].recipeIds = job.recipeIds || [];
                icon.innerHTML = '✅';
                var n = (job.recipeIds || []).length;
                status.innerHTML = n > 0
                    ? 'Готово · <a class="upload-link" onclick="openExtractionResult(\'' + job.id + '\')">Открыть рецепт →</a>'
                    : 'Готово, рецепты не найдены';
                if (n > 0) loadExtractionTitle(row, job.id);
            } else if (job.status === JOB_FAILED) {
                icon.innerHTML = '⚠️';
                status.innerHTML = escapeHtml(job.error || 'Ошибка извлечения');
            } else if (job.status === JOB_CANCELLED) {
                icon.innerHTML = '⛔';
                status.innerHTML = 'Отменено';
            }

            renderActionButtons(row, job);
        }

        // Открыть готовый результат: один рецепт — сразу, несколько — списком
        function openExtractionResult(id) {
            var e = extractionRows[id];
            if (!e || !e.recipeIds || !e.recipeIds.length) return;
            if (e.recipeIds.length === 1) {
                showRecipe(e.recipeIds[0]);
                return;
            }
            showLoading();
            Promise.all(e.recipeIds.map(function (rid) {
                return fetch('/api/recipes/' + rid).then(function (r) { return r.ok ? r.json() : null; });
            })).then(function (recipes) {
                hideLoading();
                recipes = recipes.filter(Boolean);
                var html = '<h2>Найдено рецептов: ' + recipes.length + '</h2><div class="recipe-list">';
                recipes.forEach(function (r) {
                    html += '<div class="recipe-item" onclick="showRecipe(\'' + r.id + '\')">' +
                            '<span class="recipe-item-title">' + escapeHtml(r.title) + '</span><span>➔</span></div>';
                });
                html += '</div>';
                renderResults(html);
            }).catch(function (e2) {
                hideLoading();
                renderResults('<h2>Ошибка</h2><p>' + escapeHtml(e2.message) + '</p>');
            });
        }

        function shortUrl(u) {
            try {
                var p = new URL(u);
                return p.hostname + (p.pathname.length > 40 ? p.pathname.slice(0, 37) + '…' : p.pathname);
            } catch (e) { return u.length > 48 ? u.slice(0, 45) + '…' : u; }
        }

        // Как только обработан — заменить URL на название первого рецепта
        function loadExtractionTitle(row, id) {
            var e = extractionRows[id];
            if (!e || !e.recipeIds || !e.recipeIds.length) return;
            fetch('/api/recipes/' + e.recipeIds[0])
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (recipe) {
                    if (!recipe) return;
                    var t = row.querySelector('.upload-title');
                    if (t) {
                        t.textContent = recipe.title +
                            (e.recipeIds.length > 1 ? ' (+' + (e.recipeIds.length - 1) + ')' : '');
                    }
                })
                .catch(function () { });
        }

        // Название видео через oEmbed (прогрессивное улучшение; при сбое — URL)
        function fetchVideoTitle(url) {
            return fetch('https://www.youtube.com/oembed?url=' + encodeURIComponent(url) + '&format=json')
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (d) { return d && d.title ? d.title : null; })
                .catch(function () { return null; });
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
        async function goBack() {
            // Из рецепта возвращаемся в меню — перерисовываем его с обновлёнными порциями
            if (goBackToMenu && currentPlan) {
                goBackToMenu = false;
                activeRecipeId = null;
                previousStateHtml = null;
                previousStateKind = null;
                renderResults(renderMealPlanHtml(currentPlan));
                return;
            }
            // Назад в «Мои рецепты»: снапшот списка устарел после правки/создания рецепта —
            // перерисовываем вкладку из свежего ответа, сохраняя текущий фильтр и запрос
            if (previousStateKind === 'recipes') {
                previousStateHtml = null;
                previousStateKind = null;
                activeRecipeId = null;
                try {
                    const r = await fetch('/api/recipes');
                    if (r.ok) allRecipesCache = await r.json();
                } catch (e) { /* остаёмся на кэше — список лучше устаревший, чем пустой */ }
                renderRecipesTab();
                return;
            }
            if (previousStateHtml) {
                const container = document.getElementById('resultsContainer');
                container.innerHTML = previousStateHtml;
                container.style.display = 'block';
                activeRecipeId = null;
                previousStateHtml = null;
                previousStateKind = null;
            }
        }

        // Выполнение экшена при отправке
        async function executeAction() {
            const value = document.getElementById('mainInput').value.trim();

            if (currentMode === 'youtube') {
                if (!value) return;
                try {
                    await enqueueExtraction(value);
                    const input = document.getElementById('mainInput');
                    input.value = '';
                    input.focus();
                } catch (e) {
                    alert('Ошибка: ' + e.message);
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
                    allRecipesCache = data;
                    recipesQuery = value.toLowerCase();
                    renderRecipesTab();
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
                allRecipesCache = data;
                recipesQuery = '';
                renderRecipesTab();
            } catch (e) {
                hideLoading();
                renderResults(`<h2>Ошибка</h2><p>${e.message}</p>`);
            }
        }

        // Совпадение рецепта с фильтром приёма пищи (OR: рецепт виден, если его флаг true)
        function matchesMealFilter(recipe, filter) {
            if (filter === 'breakfast') return !!recipe.isBreakfast;
            if (filter === 'lunch') return !!recipe.isLunch;
            if (filter === 'dinner') return !!recipe.isDinner;
            if (filter === 'snack') return !!recipe.isSnack;
            return true;
        }

        // Совпадение с текстовым запросом (заголовок + названия ингредиентов)
        function matchesRecipesQuery(recipe) {
            if (!recipesQuery) return true;
            if (recipe.title.toLowerCase().includes(recipesQuery)) return true;
            return (recipe.ingredientNames || []).some(function (name) {
                return name.toLowerCase().includes(recipesQuery);
            });
        }

        function setRecipesFilter(filter) {
            recipesFilter = filter;
            // Синхронизируем атрибуты checked (свойство одного radio само не сериализуется в innerHTML —
            // иначе снапшот previousStateHtml при открытии рецепта вернёт не тот фильтр)
            var radios = document.querySelectorAll('input[name="mealFilter"]');
            radios.forEach(function (rb) {
                if (rb.value === filter) rb.setAttribute('checked', '');
                else rb.removeAttribute('checked');
            });
            var main = document.querySelector('.recipes-main');
            if (main) main.innerHTML = buildRecipesMainHtml();
            else renderRecipesTab();
        }

        // Правая колонка вкладки: заголовок + список (или пустое состояние)
        function buildRecipesMainHtml() {
            var visible = (allRecipesCache || [])
                .filter(matchesRecipesQuery)
                .filter(function (r) { return matchesMealFilter(r, recipesFilter); });

            var html = '<div class="recipes-main"><div class="recipes-header">' +
                '<h2>Мои рецепты</h2>' +
                '<button type="button" class="action-btn recipes-new-btn" onclick="openRecipeForm(null)">➕ Новый рецепт</button>' +
                '</div>';
            if (visible.length > 0) {
                html += '<div class="recipe-list">';
                visible.forEach(function (recipe) {
                    html += '<div class="recipe-item" ondblclick="showRecipe(\'' + recipe.id + '\')">' +
                        '<span class="recipe-item-title" onclick="showRecipe(\'' + recipe.id + '\')">' + escapeHtml(recipe.title) + '</span>' +
                        '<div class="recipe-item-actions">' +
                        '<span class="recipe-item-open" onclick="showRecipe(\'' + recipe.id + '\')" title="Открыть">➔</span>' +
                        '<span class="recipe-item-delete" onclick="askDeleteRecipe(\'' + recipe.id + '\')" title="Удалить">🗑️</span>' +
                        '</div>' +
                        '</div>';
                });
                html += '</div>';
            } else if (!(allRecipesCache || []).length && !recipesQuery) {
                html += '<p>В вашей книге рецептов пока пусто.</p>';
            } else {
                html += '<p>Ничего не найдено.</p>';
            }
            html += '</div>';
            return html;
        }

        // Единый рендер вкладки: сайдбар-фильтр + список
        function renderRecipesTab() {
            var byQuery = (allRecipesCache || []).filter(matchesRecipesQuery);
            var counts = { all: byQuery.length, breakfast: 0, lunch: 0, dinner: 0, snack: 0 };
            byQuery.forEach(function (r) {
                if (r.isBreakfast) counts.breakfast++;
                if (r.isLunch) counts.lunch++;
                if (r.isDinner) counts.dinner++;
                if (r.isSnack) counts.snack++;
            });

            var options = [
                ['all', 'Все'],
                ['breakfast', 'Завтрак'],
                ['lunch', 'Обед'],
                ['dinner', 'Ужин'],
                ['snack', 'Перекус']
            ];

            var html = '<div class="recipes-layout"><aside class="recipes-sidebar">' +
                '<div class="recipes-sidebar-title">Приём пищи</div>';
            options.forEach(function (opt) {
                var checked = recipesFilter === opt[0] ? ' checked' : '';
                html += '<label class="meal-radio">' +
                    '<input type="radio" name="mealFilter" value="' + opt[0] + '"' + checked +
                    ' onclick="setRecipesFilter(\'' + opt[0] + '\')">' +
                    '<span class="meal-radio-label">' + opt[1] + '</span>' +
                    '<span class="meal-count">' + counts[opt[0]] + '</span>' +
                    '</label>';
            });
            html += '</aside>' + buildRecipesMainHtml() + '</div>';

            renderResults(html);
        }

        // Удаление по id: заголовок берём из кэша, чтобы не встраивать его в HTML-атрибут
        function askDeleteRecipe(id) {
            var recipe = (allRecipesCache || []).find(function (r) { return r.id === id; });
            deleteRecipe(id, recipe ? recipe.title : '');
        }

        // ---- Форма создания/редактирования рецепта ----

        // id === null → создание (список), иначе редактирование из текущего view
        function openRecipeForm(id) {
            var container = document.getElementById('resultsContainer');
            // Снапшот списка: «Отмена» при создании вернёт обратно; при редактировании
            // снапшот уже сделан showRecipe() и не должен затираться
            if (!previousStateHtml) {
                previousStateHtml = container.innerHTML;
                previousStateKind = (currentMode === 'recipes') ? 'recipes' : null;
            }

            recipeFormId = (id === null) ? '' : id;
            recipeFormFrom = (id !== null && activeRecipeId !== null) ? 'recipe' : 'list';
            recipeFormSaving = false;
            recipeFormDirty = false;
            recipeFormBaseline = '';

            var data = null;
            if (id !== null && recipeViewState.id) {
                data = {
                    title: recipeViewState.recipe.title,
                    servings: recipeViewState.recipe.servings,
                    isBreakfast: !!recipeViewState.recipe.isBreakfast,
                    isLunch: !!recipeViewState.recipe.isLunch,
                    isDinner: !!recipeViewState.recipe.isDinner,
                    isSnack: !!recipeViewState.recipe.isSnack,
                    // Ингредиенты/шаги берём из view — редактируем то, что на экране
                    ingredients: recipeViewState.ingredients || [],
                    steps: recipeViewState.steps || []
                };
            }

            renderResults(renderRecipeFormHtml(data));
            rfAfterRender(data);
        }

        function rfCheckbox(id, label, checked) {
            return '<label class="rf-check"><input type="checkbox" id="' + id + '"' +
                (checked ? ' checked' : '') + '><span>' + label + '</span></label>';
        }

        function renderRecipeFormHtml(data) {
            var isEdit = !!data;
            var html = '<div class="top-action-bar rf-topbar">' +
                '<button class="icon-btn" onclick="cancelRecipeForm()" title="Отмена">←</button>' +
                '<button class="action-btn rf-save-btn rf-save-top" onclick="saveRecipeForm()" title="Сохранить рецепт" disabled>💾 Сохранить</button>' +
                '</div>';
            html += '<h2>' + (isEdit ? 'Редактирование рецепта' : 'Новый рецепт') + '</h2>';
            html += '<div class="recipe-form" id="rfForm">';

            html += '<label class="rf-field"><span>Название</span>' +
                '<input type="text" id="rfTitle" maxlength="300" placeholder="Например: Борщ со свёклой"></label>';

            html += '<div class="rf-row">' +
                '<label class="rf-field rf-servings"><span>Порций</span>' +
                '<input type="number" id="rfServings" min="1" max="20" value="' + (isEdit ? data.servings : 2) + '"></label>' +
                '<fieldset class="rf-meals"><legend>Приём пищи</legend>' +
                rfCheckbox('rfBreakfast', 'Завтрак', isEdit && data.isBreakfast) +
                rfCheckbox('rfLunch', 'Обед', isEdit && data.isLunch) +
                rfCheckbox('rfDinner', 'Ужин', isEdit && data.isDinner) +
                rfCheckbox('rfSnack', 'Перекус', isEdit && data.isSnack) +
                '</fieldset></div>';

            html += '<h3>Ингредиенты</h3><div id="rfIngredients" class="rf-list"></div>' +
                '<button type="button" class="rf-add-btn" onclick="rfAddIngredient()">➕ Добавить ингредиент</button>';

            html += '<h3>Инструкция по приготовлению</h3><div id="rfSteps" class="rf-list"></div>' +
                '<button type="button" class="rf-add-btn" onclick="rfAddStep()">➕ Добавить шаг</button>';

            html += '<div class="rf-actions">' +
                '<button type="button" class="action-btn secondary rf-cancel-btn" onclick="cancelRecipeForm()">Отмена</button>' +
                '<button type="button" class="action-btn rf-save-btn" onclick="saveRecipeForm()" disabled>💾 Сохранить</button>' +
                '</div>';

            html += '</div>';
            return html;
        }

        // Строка ингредиента: значения кладём через .value (шаблон статический — без XSS)
        function rfAddIngredient(name, amount) {
            var box = document.getElementById('rfIngredients');
            if (!box) return;
            var row = document.createElement('div');
            row.className = 'rf-ing-row';
            row.innerHTML = '<input type="text" class="rf-ing-name" maxlength="200" placeholder="Ингредиент">' +
                '<input type="text" class="rf-ing-amount" maxlength="200" placeholder="Количество (напр. 200 г)">' +
                '<button type="button" class="rf-del-btn" onclick="rfRemoveIngredient(this)" title="Удалить строку">✕</button>';
            row.querySelector('.rf-ing-name').value = name || '';
            row.querySelector('.rf-ing-amount').value = amount || '';
            box.appendChild(row);
            rfRefreshSaveState();
        }

        function rfRemoveIngredient(btn) {
            btn.parentNode.remove();
            rfRefreshSaveState();
        }

        function rfAddStep(description) {
            var box = document.getElementById('rfSteps');
            if (!box) return;
            var row = document.createElement('div');
            row.className = 'rf-step-row';
            row.innerHTML = '<span class="rf-step-num"></span>' +
                '<textarea class="rf-step-text" rows="2" maxlength="3000" placeholder="Описание шага"></textarea>' +
                '<button type="button" class="rf-del-btn" onclick="rfRemoveStep(this)" title="Удалить шаг">✕</button>';
            row.querySelector('.rf-step-text').value = description || '';
            box.appendChild(row);
            rfRenumberSteps();
            rfRefreshSaveState();
        }

        function rfRemoveStep(btn) {
            btn.parentNode.remove();
            rfRenumberSteps();
            rfRefreshSaveState();
        }

        function rfRenumberSteps() {
            var rows = document.querySelectorAll('#rfSteps .rf-step-row');
            rows.forEach(function (row, i) {
                row.querySelector('.rf-step-num').textContent = (i + 1) + '.';
            });
        }

        // Снимок формы: единый сборщик для сравнения с префиллом и для payload
        function rfCollectForm() {
            var ingredients = [];
            document.querySelectorAll('#rfIngredients .rf-ing-row').forEach(function (row) {
                var name = row.querySelector('.rf-ing-name').value.trim();
                if (!name) return;
                ingredients.push({ name: name, amount: row.querySelector('.rf-ing-amount').value.trim() });
            });
            var steps = [];
            document.querySelectorAll('#rfSteps .rf-step-text').forEach(function (el) {
                var text = el.value.trim();
                if (text) steps.push(text);
            });
            return {
                title: (document.getElementById('rfTitle').value || '').trim(),
                // сырая строка: «2» и «02» считаем разными правками, но валидация разберёт это ниже
                servings: document.getElementById('rfServings').value,
                isBreakfast: document.getElementById('rfBreakfast').checked,
                isLunch: document.getElementById('rfLunch').checked,
                isDinner: document.getElementById('rfDinner').checked,
                isSnack: document.getElementById('rfSnack').checked,
                ingredients: ingredients,
                steps: steps
            };
        }

        function rfFormSignature() {
            var f = rfCollectForm();
            return JSON.stringify([f.title, f.servings, f.isBreakfast, f.isLunch, f.isDinner, f.isSnack, f.ingredients, f.steps]);
        }

        // «Сохранить» активна, только пока форма отличается от загруженного состояния
        function rfRefreshSaveState() {
            recipeFormDirty = rfFormSignature() !== recipeFormBaseline;
            document.querySelectorAll('.rf-save-btn').forEach(function (b) {
                b.disabled = !recipeFormDirty || recipeFormSaving;
            });
        }

        // Заполнение формы после рендера: префилл при редактировании, пустые строки при создании
        function rfAfterRender(data) {
            var titleEl = document.getElementById('rfTitle');
            if (data) {
                titleEl.value = data.title || '';
                (data.ingredients || []).forEach(function (i) { rfAddIngredient(i.name, i.amount); });
                if (!(data.ingredients || []).length) rfAddIngredient();
                (data.steps || []).forEach(function (s) { rfAddStep(s.description); });
                if (!(data.steps || []).length) rfAddStep();
            } else {
                rfAddIngredient();
                rfAddStep();
            }
            rfRenumberSteps();
            if (titleEl) titleEl.focus();

            // Базовый снимок = состояние после префилла; дальше ловим правки делегированием
            recipeFormBaseline = rfFormSignature();
            var formEl = document.getElementById('rfForm');
            if (formEl) {
                formEl.addEventListener('input', rfRefreshSaveState);
                formEl.addEventListener('change', rfRefreshSaveState);
            }
            rfRefreshSaveState();
        }

        function cancelRecipeForm() {
            recipeFormId = null;
            if (recipeFormFrom === 'recipe' && recipeViewState.id) {
                // Возврат на страницу рецепта — view не менялся, просто перерисовываем
                renderResults(renderRecipeHtml(recipeViewState.recipe));
            } else {
                goBack();
            }
        }

        // Разбор тела ошибки валидации (ValidationProblemDetails) в читаемые строки
        async function rfErrorMessage(r) {
            try {
                var body = await r.text();
                try {
                    var j = JSON.parse(body);
                    var msgs = [];
                    if (j.errors) {
                        for (var key in j.errors) msgs = msgs.concat(j.errors[key]);
                    }
                    if (msgs.length) return msgs.join('\n');
                    if (j.detail) return j.detail;
                    if (j.title) return j.title;
                } catch (e) { }
                return body || r.statusText;
            } catch (e) {
                return r.statusText;
            }
        }

        async function saveRecipeForm() {
            if (recipeFormSaving || !recipeFormDirty) return;

            var form = rfCollectForm();
            if (!form.title) {
                alert('Укажите название рецепта');
                document.getElementById('rfTitle').focus();
                return;
            }

            var servings = parseInt(form.servings, 10);
            if (isNaN(servings) || servings < 1 || servings > 20) {
                alert('Число порций должно быть от 1 до 20');
                return;
            }

            if (!form.ingredients.length) { alert('Добавьте хотя бы один ингредиент'); return; }
            if (!form.steps.length) { alert('Добавьте хотя бы один шаг'); return; }

            var payload = {
                title: form.title,
                servings: servings,
                isBreakfast: form.isBreakfast,
                isLunch: form.isLunch,
                isDinner: form.isDinner,
                isSnack: form.isSnack,
                ingredients: form.ingredients,
                steps: form.steps.map(function (text, i) { return { number: i + 1, description: text }; })
            };

            var isEdit = recipeFormId !== null && recipeFormId !== '';
            // Кнопок сохранения две (вверх справа и внизу) — глушим обе
            var saveBtns = document.querySelectorAll('.rf-save-btn');
            saveBtns.forEach(function (b) { b.disabled = true; });
            recipeFormSaving = true;
            showLoading();

            try {
                var r = await fetch(isEdit ? '/api/recipes/' + recipeFormId : '/api/recipes', {
                    method: isEdit ? 'PUT' : 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
                if (!r.ok) throw new Error(await rfErrorMessage(r));
                var data = await r.json();

                recipeFormId = null;
                recipeFormSaving = false;
                recipeFormDirty = false;
                hideLoading();

                if (isEdit) {
                    // Перезагрузка с текущим целевым числом порций: суммы и виджет консистентны,
                    // снапшот списка (previousStateHtml) не перезаписывается — activeRecipeId задан
                    showRecipe(data.id, recipeViewState.portions);
                } else {
                    // Подавляем снапшот формы в showRecipe — «←» должен вести на список
                    activeRecipeId = data.id;
                    showRecipe(data.id);
                }
            } catch (e) {
                hideLoading();
                rfRefreshSaveState(); // кнопки возвращаются в состояние «есть несохранённые правки»
                recipeFormSaving = false;
                alert('Не удалось сохранить рецепт:\n' + e.message);
            }
        }

        // Отрисовка разметки рецепта
        function renderRecipeHtml(recipe) {
            let html = `<h2>${recipe.title}</h2>`;

            // Панель действий сверху (иконки, подпись при наведении)
            html += '<div class="top-action-bar">';
            if (previousStateHtml) {
                html += `<button class="icon-btn" onclick="goBack()" title="Назад">←</button>`;
            } else {
                html += `<button class="icon-btn" onclick="switchMode('recipes', document.getElementById('mode-recipes'))" title="Ко всем рецептам">🗂️</button>`;
            }
            html += `<button class="icon-btn" onclick="openRecipeForm('${recipe.id}')" title="Редактировать">✏️</button>`;
            html += `<button class="icon-btn" onclick="exportToObsidian('${recipe.id}')" title="Сохранить в Obsidian"><svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M19.355 18.538a68.967 68.959 0 0 0 1.858-2.954.81.81 0 0 0-.062-.9c-.516-.685-1.504-2.075-2.042-3.362-.553-1.321-.636-3.375-.64-4.377a1.707 1.707 0 0 0-.358-1.05l-3.198-4.064a3.744 3.744 0 0 1-.076.543c-.106.503-.307 1.004-.536 1.5-.134.29-.29.6-.446.914l-.31.626c-.516 1.068-.997 2.227-1.132 3.59-.124 1.26.046 2.73.815 4.481.128.011.257.025.386.044a6.363 6.363 0 0 1 3.326 1.505c.916.79 1.744 1.922 2.415 3.5zM8.199 22.569c.073.012.146.02.22.02.78.024 2.095.092 3.16.29.87.16 2.593.64 4.01 1.055 1.083.316 2.198-.548 2.355-1.664.114-.814.33-1.735.725-2.58l-.01.005c-.67-1.87-1.522-3.078-2.416-3.849a5.295 5.295 0 0 0-2.778-1.257c-1.54-.216-2.952.19-3.84.45.532 2.218.368 4.829-1.425 7.531zM5.533 9.938c-.023.1-.056.197-.098.29L2.82 16.059a1.602 1.602 0 0 0 .313 1.772l4.116 4.24c2.103-3.101 1.796-6.02.836-8.3-.728-1.73-1.832-3.081-2.55-3.831zM9.32 14.01c.615-.183 1.606-.465 2.745-.534-.683-1.725-.848-3.233-.716-4.577.154-1.552.7-2.847 1.235-3.95.113-.235.223-.454.328-.664.149-.297.288-.577.419-.86.217-.47.379-.885.46-1.27.08-.38.08-.72-.014-1.043-.095-.325-.297-.675-.68-1.06a1.6 1.6 0 0 0-1.475.36l-4.95 4.452a1.602 1.602 0 0 0-.513.952l-.427 2.83c.672.59 2.328 2.316 3.335 4.711.09.21.175.43.253.653z"/></svg></button>`;
            html += `<button class="icon-btn" id="copyMdBtn" onclick="copyRecipeText('${recipe.id}')" title="Скопировать текст">📋</button>`;
            html += '</div>';

            // Помечено как изменённый рецепт — баннер с кнопкой «Сохранить как вариант»
            if (recipeViewState.modified && !recipeViewState.sourceId) {
                html += '<div class="recipe-modified-banner">' +
                    '<span>✏️ Рецепт изменён</span>' +
                    `<button class="action-btn" onclick="saveAsVariant()">➕ Сохранить как вариант</button>` +
                    '</div>';
            }

            // Чипы-редакции (Оригинал + вариации группы)
            var variants = recipe.variants || recipeViewState.variants || [];
            if (variants && variants.length > 0) {
                html += '<div class="recipe-variant-chips">';
                variants.forEach(function (v) {
                    var active = v.id === recipe.id ? ' active' : '';
                    var label = v.variantTitle || 'Оригинал';
                    html += '<span class="recipe-variant-chip' + active + '" onclick="showRecipeVariant(\'' + v.id + '\')">' + escapeHtml(label) + '</span>';
                });
                html += '</div>';
            }

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

            html += '<h3>Инструкция по приготовлению</h3><ol id="recipeStepsList">';
            (recipeViewState.steps && recipeViewState.steps.length ? recipeViewState.steps : recipe.steps).forEach(function (s) { html += '<li>' + escapeHtml(s.description) + '</li>'; });
            html += '</ol>';

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
                    item: i,
                    modified: false,
                    substitutions: []
                };

                html += buildMealCardHtml(i, itemId);
            });

            html += '</div>';

            // Группа кнопок: список покупок и генерация нового меню
            html += `<div class="btn-group meal-plan-actions">
                        <button class="action-btn" onclick="showShoppingList('${plan.id}')">🛒 Список покупок</button>
                        <button class="action-btn secondary" onclick="regenerateMenu()">🔄 Сгенерировать заново</button>
                     </div>`;
            return html;
        }

        // Отрисовка одной карточки меню: заголовок, варианты группы, баннер «изменён», ингредиенты
        function buildMealCardHtml(item, itemId) {
            var st = menuViewState[itemId] || {};
            var portions = st.portions != null ? st.portions : item.portions;
            var ingredients = (st.ingredients || item.ingredients || []);

            var html = '<div class="meal-card" id="mealCard_' + itemId + '">';
            html += '<div class="meal-card-header">' +
                '<span class="meal-type">' + escapeHtml(item.mealType) + '</span>' +
                '<span class="meal-recipe-btn" onclick="openMenuRecipe(\'' + itemId + '\')">' + escapeHtml(item.recipe.title) + '</span>' +
                '<div class="meal-portions-widget" title="Пересчитать количество ингредиентов">' +
                '<span>🍽️</span>' +
                '<button class="portion-btn" onclick="menuPortionsChange(\'' + itemId + '\', -1)">−</button>' +
                '<span class="meal-portions-value" id="menuPortionsVal_' + itemId + '">' + portions + '</span>' +
                '<button class="portion-btn" onclick="menuPortionsChange(\'' + itemId + '\', 1)">+</button>' +
                '<button class="portion-btn meal-recalc-btn" onclick="llmMenuRescale(\'' + itemId + '\')" title="Пересчитать ингредиенты">↻</button>' +
                '</div></div>';

            var variants = item.variants || [];
            if (variants.length > 0) {
                html += '<div class="recipe-variant-chips">';
                variants.forEach(function (v) {
                    var active = (v.id === item.recipe.id && !st.modified) ? ' active' : '';
                    var label = escapeHtml(v.variantTitle || 'Оригинал');
                    html += '<span class="recipe-variant-chip' + active + '" onclick="switchMenuRecipeVariant(\'' + itemId + '\', \'' + v.id + '\')">' + label + '</span>';
                });
                html += '</div>';
            }

            if (st.modified) {
                html += '<div class="recipe-modified-banner">' +
                    '<span>✏️ Рецепт изменён</span>' +
                    '<button class="action-btn" onclick="saveMenuVariant(\'' + itemId + '\')">➕ Сохранить как вариант</button>' +
                    '</div>';
            }

            html += '<ul class="meal-ingredients" id="menuIngredients_' + itemId + '">' +
                buildMenuIngredientItems(ingredients, itemId, st.recipeId || item.recipe.id) +
                '</ul>';
            html += '</div>';
            return html;
        }

        // Перерисовка одной карточки меню из её состояния
        function rerenderMealCard(itemId) {
            var st = menuViewState[itemId];
            var card = document.getElementById('mealCard_' + itemId);
            if (!st || !card) return;
            card.outerHTML = buildMealCardHtml(st.item, itemId);
        }

        // Сохранение изменённого блюда карточки меню как варианта рецепта + переключение пункта на вариант
        async function saveMenuVariant(itemId) {
            var st = menuViewState[itemId];
            if (!st || st.busy) return;

            var title = prompt('Название варианта (например: "без сметаны"):');
            if (title === null) return;

            st.busy = true;
            var card = document.getElementById('mealCard_' + itemId);
            if (card) card.classList.add('is-busy');
            showLoading();
            try {
                const res = await fetch('/api/recipes/' + st.recipeId + '?servings=' + st.portions);
                if (!res.ok) throw new Error('Не удалось загрузить рецепт');
                const recipe = await res.json();

                var ingredients = (st.ingredients || []).map(function (x) { return { name: x.name, amount: x.amount || '' }; });

                var steps = recipe.steps || [];
                var tips = recipe.preparationTips || null;
                if (st.substitutions && st.substitutions.length) {
                    var rewritten = await batchRewriteSteps(st.recipeId, st.substitutions);
                    if (rewritten) {
                        if (rewritten.steps.length) steps = rewritten.steps;
                        if (rewritten.tips) tips = rewritten.tips;
                    }
                }
                var stepsForVariant = (steps || []).map(function (s) { return { number: s.number, description: s.description }; });

                const r = await fetch('/api/recipes/' + st.recipeId + '/variants', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ variantTitle: title, ingredients: ingredients, steps: stepsForVariant, preparationTips: tips })
                });
                if (!r.ok) throw new Error('Не удалось сохранить вариант');
                const created = await r.json();

                const p = await fetch('/api/mealplans/items/' + itemId, {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ portions: st.portions, recipeId: created.id })
                });
                if (!p.ok) throw new Error('Не удалось применить вариант к пункту меню');
                const updated = await p.json();

                st.recipeId = updated.recipe.id;
                st.ingredients = updated.ingredients || [];
                st.modified = false;
                if (st.item) {
                    st.item.recipe = updated.recipe;
                    st.item.ingredients = st.ingredients.slice();
                }

                rerenderMealCard(itemId);
            } catch (e) {
                alert('Ошибка: ' + e.message);
            } finally {
                st.busy = false;
                if (card) card.classList.remove('is-busy');
                hideLoading();
            }
        }
        function buildMenuIngredientItems(ingredients, itemId, recipeId) {
            var html = '';
            ingredients.forEach(function (i) {
                var name = escapeHtml(i.name);
                html += '<li>' +
                    '<span class="ingredient-link" data-recipe="' + recipeId + '" data-ingredient="' + name + '" onclick="substituteMenuIngredient(this, \'' + itemId + '\')">' + name + '</span>' +
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
                const r = await fetch(`/api/recipes/${st.recipeId}?servings=${st.portions}`);
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
                previousStateKind = (currentMode === 'recipes') ? 'recipes' : null;
            }

            hideResults();
            activeRecipeId = id;
            try {
                const url = servingsOverride != null
                    ? `/api/recipes/${id}?servings=${servingsOverride}`
                    : `/api/recipes/${id}`;
                const r = await fetch(url);
                if (!r.ok) throw new Error('Рецепт не найден');
                const recipe = await r.json();

                currentRecipeObj = recipe;
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
            showLoading();
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
            } finally {
                hideLoading();
            }
        }

        async function copyRecipeText(id) {
            showLoading();
            try {
                const r = await fetch(`/api/recipes/${id}/text`);
                if (!r.ok) throw new Error('Не удалось получить текст рецепта');
                await copyTextToClipboard(await r.text(), 'copyMdBtn');
            } catch (e) {
                alert('Ошибка: ' + e.message);
            } finally {
                hideLoading();
            }
        }

        // Копирование текста в буфер с обратной связью на кнопке (без модалок)
        async function copyTextToClipboard(text, btnId) {
            var ok = false;
            if (navigator.clipboard && window.isSecureContext) {
                try {
                    await navigator.clipboard.writeText(text);
                    ok = true;
                } catch (e) { }
            }
            if (!ok) {
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                ok = document.execCommand('copy');
                document.body.removeChild(ta);
            }
            if (!ok) throw new Error('Не удалось скопировать в буфер обмена');

            var btn = document.getElementById(btnId);
            if (btn) {
                var oldTitle = btn.title;
                btn.title = 'Скопировано ✓';
                setTimeout(function () { btn.title = oldTitle; }, 1500);
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

                _shoppingListText = text;

                // Конвертируем markdown в HTML (звёздочки → жирный, • → маркеры)
                var html = text
                    .replace(/\*(.*?)\*/g, '<b>$1</b>')
                    .replace(/^• /gm, '&bull; ')
                    .replace(/\n/g, '<br>');

                renderResults(`
                    <div class="top-action-bar">
                        <button class="icon-btn" onclick="loadCurrentMenu()" title="Назад к меню">←</button>
                        <button class="icon-btn" id="copyListBtn" onclick="copyShoppingListText()" title="Скопировать текст">📋</button>
                    </div>
                    <h2>Список покупок</h2>
                    <div style="line-height: 1.8; color: var(--text-main); font-size: 15px;">${html}</div>
                `);
            } catch (e) {
                hideLoading();
                setMenuLocked(false);
                alert('Ошибка: ' + e.message);
            }
        }

        function copyShoppingListText() {
            copyTextToClipboard((_shoppingListText || '').replace(/\*/g, ''), 'copyListBtn')
                .catch(function (e) { alert('Ошибка: ' + e.message); });
        }

        // Блокировка изменения карточек меню и кнопок, пока собирается список покупок
        function setMenuLocked(locked) {
            document.querySelectorAll('.meal-card').forEach(function (card) {
                if (locked) card.classList.add('is-busy');
                else card.classList.remove('is-busy');
            });
            document.querySelectorAll('.meal-plan-actions').forEach(function (group) {
                if (locked) group.classList.add('is-busy');
                else group.classList.remove('is-busy');
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
                '<input type="text" id="customSubstituteInput" maxlength="100" placeholder="Свой вариант..." onkeydown="if(event.key===\'Enter\') selectCustomSubstitute()">' +
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
            if (!val || val.length > 100) return;
            applySubstitution(val);
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

            var originalFromRecipe = originalName;

            // Замена на карточке меню — обновляем состояние пункта меню и включаем режим «изменён».
                // Шаги и советы переписываются ОДНИМ LLM-вызовом позже, при «Сохранить как вариант».
                if (substituteMenuItemId && menuViewState[substituteMenuItemId]) {
                var st = menuViewState[substituteMenuItemId];
                (st.ingredients || []).forEach(function (ing) {
                    if (ing.name === originalName && ing.originalName) originalFromRecipe = ing.originalName;
                });
                (st.ingredients || []).forEach(function (ing) {
                    if (ing.name === originalName) {
                        if (!ing.originalName) ing.originalName = originalFromRecipe;
                        ing.name = newName;
                    }
                });
                st.modified = true;
                st.substitutions = st.substitutions || [];
                var mi = st.substitutions.findIndex(function (x) { return x.original === originalFromRecipe; });
                if (mi >= 0) st.substitutions[mi].replacement = newName;
                else st.substitutions.push({ original: originalFromRecipe, replacement: newName });
                if (st.item) st.item.ingredients = st.ingredients.slice();
                rerenderMealCard(substituteMenuItemId);
                closeSubstitutePopover();
                return;
            }

            // Было ли это замена уже применённая ранее (originalName хранит исходное имя из рецепта)
            if (recipeViewState.ingredients) {
                (recipeViewState.ingredients || []).forEach(function (ri) {
                    if (ri.name === originalName && ri.originalName) originalFromRecipe = ri.originalName;
                });
            }

            // Синхронизируем состояние страницы рецепта, чтобы при повторных заменах
            // сохранялся исходный originalName (настоящее имя из рецепта)
            if (recipeViewState.ingredients) {
                recipeViewState.ingredients.forEach(function (ri) {
                    if (ri.name === originalName) {
                        if (!ri.originalName) ri.originalName = originalFromRecipe;
                        ri.name = newName;
                    }
                });
            }

            // Замена всегда переводит рецепт в режим «изменён» — и на странице рецепта,
            // и когда рецепт открыт из меню (сохранить как вариант, затем переключить пункт меню)
            if (recipeViewState.id) {
                recipeViewState.modified = true;
                recipeViewState.substitutions = recipeViewState.substitutions || [];
                var si = recipeViewState.substitutions.findIndex(function (x) { return x.original === originalFromRecipe; });
                if (si >= 0) recipeViewState.substitutions[si].replacement = newName;
                else recipeViewState.substitutions.push({ original: originalFromRecipe, replacement: newName });
            }

            rerenderRecipeCard();
            closeSubstitutePopover();
        }

        // Перерисовка карточки рецепта из текущего состояния (после замены/перезаписи шагов)
        function rerenderRecipeCard() {
            if (recipeViewState.recipe) {
                renderResults(renderRecipeHtml(recipeViewState.recipe));
            }
        }

        // Батч-перегенерация шагов и советов: один LLM-вызов по всем накопленным заменам.
        // Возвращает { steps, tips } либо null при сбое.
        async function batchRewriteSteps(recipeId, substitutions) {
            if (!substitutions || !substitutions.length) return null;
            try {
                const r = await fetch('/api/recipes/' + recipeId + '/rewrite-steps', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ substitutions: substitutions.map(function (s) { return { ingredient: s.original, replacement: s.replacement }; }) })
                });
                if (!r.ok) throw new Error('Не удалось переписать шаги');
                const data = await r.json();
                return { steps: data.steps || [], tips: data.tips || null };
            } catch (e) {
                return null;
            }
        }

        // Сохранение числа порций пункта в БД
        function patchMenuItem(itemId, st) {
            fetch(`/api/mealplans/items/${itemId}`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ portions: st.portions })
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

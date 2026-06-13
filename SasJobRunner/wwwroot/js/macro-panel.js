/**
 * macro-panel.js
 * Inline-editable macro variable panel with %let submission.
 * Requirements: 8.2, 8.3, 8.4, 8.5
 */

(function () {
    'use strict';

    // ── State ─────────────────────────────────────────────────────────────────

    /** @type {Object.<string, string>} name → value map */
    let macroVars = {};

    /** Session ID read from the editor container or meta tag. */
    let sessionId = '';

    // ── Session ID resolution ─────────────────────────────────────────────────

    function resolveSessionId() {
        // Try <meta name="session-id"> first
        const meta = document.querySelector('meta[name="session-id"]');
        if (meta && meta.getAttribute('content')) {
            return meta.getAttribute('content');
        }

        // Try hidden input #session-id
        const input = document.getElementById('session-id');
        if (input && input.value) {
            return input.value;
        }

        // Try data-session-id on #editor-container (available on Editor page)
        const editorContainer = document.getElementById('editor-container');
        if (editorContainer && editorContainer.getAttribute('data-session-id')) {
            return editorContainer.getAttribute('data-session-id');
        }

        return '';
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /**
     * Clears and re-renders #macro-panel with the current macroVars state.
     * Each row has: var name (static), value input, Save button, error span.
     */
    function renderMacroPanel() {
        const panel = document.getElementById('macro-panel');
        if (!panel) return;

        panel.innerHTML = '';

        const entries = Object.entries(macroVars);

        if (entries.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'macro-empty';
            empty.textContent = 'No macro variables in this session.';
            panel.appendChild(empty);
            return;
        }

        entries.forEach(function ([name, value]) {
            const row = document.createElement('div');
            row.className = 'macro-row';
            row.setAttribute('data-name', name);

            // Variable name label
            const nameEl = document.createElement('span');
            nameEl.className = 'macro-name';
            nameEl.textContent = name;

            // Value input
            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'macro-value-input';
            input.value = value;
            input.setAttribute('aria-label', `Value for ${name}`);

            // Save button
            const saveBtn = document.createElement('button');
            saveBtn.type = 'button';
            saveBtn.className = 'macro-save-btn';
            saveBtn.textContent = 'Save';

            // Inline error span
            const errorSpan = document.createElement('span');
            errorSpan.className = 'macro-error';
            errorSpan.setAttribute('role', 'alert');

            // Save click handler
            saveBtn.addEventListener('click', async function () {
                const newValue = input.value;
                errorSpan.textContent = '';
                saveBtn.disabled = true;

                try {
                    const sourceCode = `%let ${name} = ${newValue};`;
                    const response = await fetch('/api/session-jobs', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ sessionId, sourceCode })
                    });

                    if (response.ok) {
                        // Update local state on success
                        macroVars[name] = newValue;
                        errorSpan.textContent = '';
                    } else {
                        const errText = await response.text();
                        errorSpan.textContent = errText || `Error ${response.status}`;
                    }
                } catch (err) {
                    errorSpan.textContent = 'Request failed: ' + err.message;
                } finally {
                    saveBtn.disabled = false;
                }
            });

            row.appendChild(nameEl);
            row.appendChild(input);
            row.appendChild(saveBtn);
            row.appendChild(errorSpan);
            panel.appendChild(row);
        });
    }

    // ── Fetch macro vars ──────────────────────────────────────────────────────

    /**
     * Fetches macro vars from /api/macro-vars?sessionId=... and re-renders.
     */
    async function fetchMacroVars() {
        if (!sessionId) return;

        try {
            const response = await fetch(`/api/macro-vars?sessionId=${encodeURIComponent(sessionId)}`);
            if (!response.ok) {
                console.warn('fetchMacroVars failed:', response.status);
                return;
            }

            const data = await response.json();

            // API may return an array of {name, value} or a plain object
            if (Array.isArray(data)) {
                macroVars = {};
                data.forEach(function (item) {
                    if (item.name != null) {
                        macroVars[item.name] = item.value ?? '';
                    }
                });
            } else if (data && typeof data === 'object') {
                macroVars = Object.assign({}, data);
            }

            renderMacroPanel();
        } catch (err) {
            console.warn('fetchMacroVars error:', err);
        }
    }

    // ── Initialise ────────────────────────────────────────────────────────────

    document.addEventListener('DOMContentLoaded', function () {
        sessionId = resolveSessionId();
        fetchMacroVars();
    });

    // ── Public API ────────────────────────────────────────────────────────────

    // Exposed so log-viewer.js can call window.refreshMacroPanel() on JobComplete
    window.refreshMacroPanel = fetchMacroVars;

}());

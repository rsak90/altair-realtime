/**
 * editor.js
 * Monaco editor initialisation and job submission.
 * Requirements: 10.1, 10.2, 10.3
 */

(function () {
    'use strict';

    // ── Monaco loader config ─────────────────────────────────────────────────

    require.config({
        paths: {
            vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs'
        }
    });

    let editor = null;

    require(['vs/editor/editor.main'], function () {
        const editorContainer = document.getElementById('editor-container');
        if (!editorContainer) return;

        // Read initial code from data attribute (set by EditorViewModel)
        let initialCode = '';
        try {
            const raw = editorContainer.getAttribute('data-initial-code');
            if (raw) {
                initialCode = JSON.parse(raw);
            }
        } catch (e) {
            console.warn('Could not parse data-initial-code:', e);
        }

        // Check if the history view stored code to load
        const storedCode = sessionStorage.getItem('editorLoadCode');
        if (storedCode) {
            initialCode = storedCode;
            sessionStorage.removeItem('editorLoadCode');
        }

        editor = monaco.editor.create(editorContainer, {
            value: initialCode,
            language: 'sas',
            theme: 'vs-dark',
            automaticLayout: true
        });

        // ── Submit button ────────────────────────────────────────────────────

        const submitBtn = document.getElementById('btn-submit');
        const submitError = document.getElementById('submit-error');

        if (submitBtn) {
            submitBtn.addEventListener('click', async function () {
                if (!editor) return;

                const sourceCode = editor.getValue();

                // Read sessionId from the editor container data attribute
                const sessionId = editorContainer.getAttribute('data-session-id') || '';

                if (!sessionId) {
                    showSubmitError('No active session. Please create or resume a session first.');
                    return;
                }

                submitBtn.disabled = true;
                clearSubmitError();

                try {
                    const response = await fetch('/api/session-jobs', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({ sessionId, sourceCode })
                    });

                    if (response.ok) {
                        const data = await response.json();
                        const jobId = data.jobId;

                        // Hand off to log-viewer.js
                        if (typeof window.joinJob === 'function') {
                            window.joinJob(jobId);
                        }
                    } else {
                        const errorText = await response.text();
                        showSubmitError(errorText || `HTTP ${response.status}`);
                    }
                } catch (err) {
                    showSubmitError('Request failed: ' + err.message);
                } finally {
                    submitBtn.disabled = false;
                }
            });
        }
    });

    // ── Helper functions ──────────────────────────────────────────────────────

    function showSubmitError(msg) {
        const el = document.getElementById('submit-error');
        if (el) {
            el.textContent = msg;
            el.style.display = '';
        } else {
            console.error('Submit error:', msg);
        }
    }

    function clearSubmitError() {
        const el = document.getElementById('submit-error');
        if (el) {
            el.textContent = '';
            el.style.display = 'none';
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /**
     * Sets the editor content. Called by the history view to reload past code.
     * @param {string} code
     */
    function loadCode(code) {
        if (editor) {
            editor.setValue(code);
        } else {
            // Monaco not yet ready — store in sessionStorage as fallback
            sessionStorage.setItem('editorLoadCode', code);
        }
    }

    window.loadCode = loadCode;

    // ── Tab switching ─────────────────────────────────────────────────────────

    /**
     * Initialize tab switching for Log and Files panels
     */
    function initTabs() {
        const tabs = document.querySelectorAll('.panel-tab');
        
        tabs.forEach(tab => {
            tab.addEventListener('click', function () {
                const targetTab = this.getAttribute('data-tab');
                
                // Update tab buttons
                tabs.forEach(t => t.classList.remove('active'));
                this.classList.add('active');
                
                // Update tab content panels
                const logPanel = document.getElementById('log-panel');
                const filesPanel = document.getElementById('files-panel');
                
                if (targetTab === 'log') {
                    logPanel.classList.add('active');
                    filesPanel.classList.remove('active');
                } else if (targetTab === 'files') {
                    filesPanel.classList.add('active');
                    logPanel.classList.remove('active');
                }
            });
        });
    }

    // Initialize tabs when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initTabs);
    } else {
        initTabs();
    }

}());

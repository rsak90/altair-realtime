/**
 * log-viewer.js
 * Real-time log streaming via SignalR with SSE fallback.
 * Requirements: 5.2, 5.4, 6.1, 6.2, 6.3, 6.4
 */

// jobId is set externally by joinJob() (called from editor.js after job submission)
let jobId = null;

const logContainer = document.getElementById('log-container');
const jumpToErrorBtn = document.getElementById('btn-jump-error');

// ── SignalR connection ───────────────────────────────────────────────────────

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/log')
    .withAutomaticReconnect()
    .build();

// Handle incoming log lines
connection.on('ReceiveLog', appendLine);

// Handle job completion
connection.on('JobComplete', () => markJobDone());

// Handle job error
connection.on('JobError', msg => markJobError(msg));

// Start the connection; fall back to SSE if SignalR is unavailable
connection.start()
    .then(() => {
        if (jobId) {
            connection.invoke('JoinJob', jobId).catch(err => {
                console.error('SignalR JoinJob failed:', err);
            });
        }
    })
    .catch(err => {
        console.warn('SignalR unavailable, falling back to SSE:', err);
        if (jobId) {
            startSseFallback(jobId);
        }
    });

// ── SSE fallback ─────────────────────────────────────────────────────────────

function startSseFallback(id) {
    const evtSource = new EventSource(`/api/session-jobs/${id}/log-stream`);
    evtSource.onmessage = e => appendLine(e.data);
    evtSource.onerror = () => {
        console.error('SSE connection error for job', id);
        evtSource.close();
    };
}

// ── Core log functions ────────────────────────────────────────────────────────

/**
 * Appends a single log line to #log-container with appropriate CSS class.
 * Auto-scrolls to bottom and updates the Jump-to-Error button state.
 * @param {string} text
 */
function appendLine(text) {
    if (!logContainer) return;

    const div = document.createElement('div');
    div.textContent = text;

    const trimmed = text.trimStart().toUpperCase();
    if (trimmed.startsWith('ERROR')) {
        div.className = 'log-error';
    } else if (trimmed.startsWith('WARNING')) {
        div.className = 'log-warning';
    } else if (trimmed.startsWith('NOTE')) {
        div.className = 'log-note';
    }

    logContainer.appendChild(div);
    logContainer.scrollTop = logContainer.scrollHeight;
    updateJumpToError();
}

/**
 * Appends a styled "Job completed" line to the log.
 */
function markJobDone() {
    if (!logContainer) return;
    const div = document.createElement('div');
    div.textContent = '--- Job completed ---';
    div.className = 'log-note';
    logContainer.appendChild(div);
    logContainer.scrollTop = logContainer.scrollHeight;

    // Refresh macro panel if available
    if (typeof window.refreshMacroPanel === 'function') {
        window.refreshMacroPanel();
    }
}

/**
 * Appends an error-styled line to the log.
 * @param {string} msg
 */
function markJobError(msg) {
    if (!logContainer) return;
    const div = document.createElement('div');
    div.textContent = `ERROR: ${msg}`;
    div.className = 'log-error';
    logContainer.appendChild(div);
    logContainer.scrollTop = logContainer.scrollHeight;
    updateJumpToError();
}

/**
 * Enables #btn-jump-error if any .log-error lines exist; disables otherwise.
 */
function updateJumpToError() {
    if (!jumpToErrorBtn) return;
    const hasError = logContainer && logContainer.querySelector('.log-error') !== null;
    jumpToErrorBtn.disabled = !hasError;
}

// ── Jump-to-Error button ──────────────────────────────────────────────────────

if (jumpToErrorBtn) {
    jumpToErrorBtn.addEventListener('click', () => {
        if (!logContainer) return;
        const firstError = logContainer.querySelector('.log-error');
        if (firstError) {
            firstError.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    });
}

// Initialise button state on load
updateJumpToError();

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Called by editor.js after a job is submitted.
 * Sets jobId and joins the SignalR group (or starts SSE if SignalR failed).
 * @param {string} id
 */
function joinJob(id) {
    jobId = id;

    // Clear previous log output for the new job
    if (logContainer) {
        logContainer.innerHTML = '';
    }
    updateJumpToError();

    if (connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke('JoinJob', id).catch(err => {
            console.error('SignalR JoinJob failed:', err);
            startSseFallback(id);
        });
    } else if (connection.state !== signalR.HubConnectionState.Connecting &&
               connection.state !== signalR.HubConnectionState.Reconnecting) {
        // SignalR never connected — use SSE directly
        startSseFallback(id);
    }
    // If Connecting/Reconnecting, the .then() on connection.start() will pick up the jobId
}

// Expose joinJob so editor.js can call window.joinJob(jobId)
window.joinJob = joinJob;

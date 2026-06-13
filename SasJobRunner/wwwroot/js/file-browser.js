/**
 * file-browser.js
 * Working Dataset File Browser
 * Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9
 */

(function () {
    let currentJobId = null;
    let currentDataset = null;
    let currentSortColumn = null;
    let currentSortAscending = true;
    let currentPage = 1;
    const rowsPerPage = 100;

    /**
     * Initializes the file browser and sets up event handlers
     */
    function init() {
        refreshFileList();
        
        // Listen for JobComplete events to refresh file list
        if (window.logViewerHub && window.logViewerHub.connection) {
            window.logViewerHub.connection.on("JobComplete", function () {
                refreshFileList();
            });
        }

        // Setup modal close handlers
        const modal = document.getElementById("dataset-viewer-modal");
        if (modal) {
            const closeBtn = modal.querySelector(".modal-close");
            if (closeBtn) {
                closeBtn.addEventListener("click", closeModal);
            }
            
            // Close on overlay click
            modal.addEventListener("click", function (e) {
                if (e.target === modal) {
                    closeModal();
                }
            });
        }
    }

    /**
     * Fetches the list of dataset files from the API and renders them
     * Requirements: 8.1, 8.2
     */
    function refreshFileList() {
        fetch("/api/files")
            .then(response => {
                if (!response.ok) throw new Error("Failed to fetch file list");
                return response.json();
            })
            .then(files => {
                renderFileList(files);
            })
            .catch(error => {
                console.error("Error fetching file list:", error);
                const container = document.getElementById("files-list");
                if (container) {
                    container.innerHTML = '<div class="no-files">Error loading files</div>';
                }
            });
    }

    /**
     * Renders the file list in the UI
     * Requirements: 8.1, 8.10
     */
    function renderFileList(files) {
        const container = document.getElementById("files-list");
        if (!container) return;

        if (files.length === 0) {
            container.innerHTML = '<div class="no-files">No dataset files found in session working directory</div>';
            return;
        }

        let html = '<div class="file-list">';
        files.forEach(file => {
            const sizeKB = (file.sizeBytes / 1024).toFixed(1);
            const dateStr = new Date(file.lastModified).toLocaleString();
            
            html += `
                <div class="file-item" data-filename="${escapeHtml(file.name)}">
                    <div class="file-name" title="View ${escapeHtml(file.name)}">${escapeHtml(file.name)}</div>
                    <div class="file-size">${sizeKB} KB</div>
                    <div class="file-date">${dateStr}</div>
                </div>
            `;
        });
        html += '</div>';

        container.innerHTML = html;

        // Attach click handlers to each file item
        container.querySelectorAll(".file-item").forEach(item => {
            item.addEventListener("click", function () {
                const fileName = this.getAttribute("data-filename");
                viewDataset(fileName);
            });
        });
    }

    /**
     * Submits a dataset introspection job and subscribes to its log stream
     * Requirements: 8.2, 8.4
     */
    function viewDataset(fileName) {
        currentDataset = fileName;
        currentJobId = null;
        currentSortColumn = null;
        currentSortAscending = true;
        currentPage = 1;

        // Show modal with loading state
        showModal("Loading dataset...", true);

        fetch(`/api/files/${encodeURIComponent(fileName)}/view`, {
            method: "POST"
        })
            .then(response => {
                if (!response.ok) throw new Error("Failed to submit dataset view job");
                return response.json();
            })
            .then(data => {
                currentJobId = data.jobId;
                subscribeToJob(data.jobId);
            })
            .catch(error => {
                console.error("Error submitting dataset view job:", error);
                showModal("Error: Failed to submit dataset view job", false);
            });
    }

    /**
     * Subscribes to a job's log stream via SignalR
     * Requirements: 8.2, 8.4, 8.5
     */
    function subscribeToJob(jobId) {
        if (!window.logViewerHub || !window.logViewerHub.connection) {
            showModal("Error: SignalR connection not available", false);
            return;
        }

        const connection = window.logViewerHub.connection;
        
        // Join the job's SignalR group
        connection.invoke("JoinJob", jobId).catch(err => {
            console.error("Error joining job group:", err);
        });

        // Collect log lines as they arrive
        const logLines = [];
        
        const receiveLogHandler = function (line) {
            if (window.currentFileBrowserJobId === jobId) {
                logLines.push(line);
            }
        };

        const jobCompleteHandler = function () {
            if (window.currentFileBrowserJobId === jobId) {
                // Parse and display the dataset
                parseAndDisplayDataset(logLines);
                
                // Leave the job group
                connection.invoke("LeaveJob", jobId).catch(err => {
                    console.error("Error leaving job group:", err);
                });
            }
        };

        const jobErrorHandler = function (errorMessage) {
            if (window.currentFileBrowserJobId === jobId) {
                showModal(`Error: ${errorMessage}`, false);
                connection.invoke("LeaveJob", jobId).catch(err => {
                    console.error("Error leaving job group:", err);
                });
            }
        };

        // Store handlers so we can remove them later
        window.currentFileBrowserJobId = jobId;
        window.currentFileBrowserHandlers = {
            receiveLog: receiveLogHandler,
            jobComplete: jobCompleteHandler,
            jobError: jobErrorHandler
        };

        connection.on("ReceiveLog", receiveLogHandler);
        connection.on("JobComplete", jobCompleteHandler);
        connection.on("JobError", jobErrorHandler);
    }

    /**
     * Parses PROC CONTENTS and PROC PRINT output and displays in modal
     * Requirements: 8.5, 8.6
     */
    function parseAndDisplayDataset(logLines) {
        try {
            // Extract columns from PROC CONTENTS output
            const columns = extractColumnsFromContents(logLines);
            
            // Extract rows from PROC PRINT output
            const rows = extractRowsFromPrint(logLines);

            if (columns.length === 0 || rows.length === 0) {
                showModal("Error: Could not parse dataset contents", false);
                return;
            }

            // Store the parsed data for sorting and pagination
            window.currentDatasetData = { columns, rows };

            renderDatasetTable();
        } catch (error) {
            console.error("Error parsing dataset:", error);
            showModal("Error: Failed to parse dataset output", false);
        }
    }

    /**
     * Extracts column names from PROC CONTENTS output
     */
    function extractColumnsFromContents(logLines) {
        const columns = [];
        let inVarSection = false;

        for (let i = 0; i < logLines.length; i++) {
            const line = logLines[i];
            
            // Look for the variables section header
            if (line.includes("Variables in Creation Order") || 
                line.includes("Alphabetic List of Variables")) {
                inVarSection = true;
                continue;
            }

            if (inVarSection) {
                // Stop at procedure end or blank line sequences
                if (line.trim() === "" || line.includes("NOTE:")) {
                    break;
                }

                // Parse variable lines (format: # Name Type Len)
                const match = line.match(/^\s*(\d+)\s+(\S+)\s+(Num|Char)\s+(\d+)/);
                if (match) {
                    columns.push(match[2]);
                }
            }
        }

        return columns;
    }

    /**
     * Extracts data rows from PROC PRINT output
     */
    function extractRowsFromPrint(logLines) {
        const rows = [];
        let inDataSection = false;
        let headerSeen = false;

        for (let i = 0; i < logLines.length; i++) {
            const line = logLines[i];

            // Look for PROC PRINT header
            if (line.includes("PROC PRINT") || line.includes("Obs")) {
                inDataSection = true;
                headerSeen = true;
                continue;
            }

            if (inDataSection) {
                // Stop at procedure end
                if (line.includes("NOTE:")) {
                    break;
                }

                // Skip separator lines and blank lines
                if (line.trim() === "" || /^[\s-]+$/.test(line)) {
                    continue;
                }

                // Parse data rows (whitespace-separated values)
                const values = line.trim().split(/\s+/);
                if (values.length > 0 && headerSeen) {
                    // Skip the Obs column (first value)
                    const rowData = values.slice(1);
                    if (rowData.length > 0) {
                        rows.push(rowData);
                    }
                }
            }
        }

        return rows;
    }

    /**
     * Renders the dataset table with current sort and pagination
     * Requirements: 8.3, 8.6, 8.7
     */
    function renderDatasetTable() {
        const data = window.currentDatasetData;
        if (!data) return;

        let { columns, rows } = data;

        // Apply sorting if a column is selected
        if (currentSortColumn !== null) {
            rows = sortRows(rows, currentSortColumn, currentSortAscending);
        }

        // Apply pagination
        const totalRows = rows.length;
        const totalPages = Math.ceil(totalRows / rowsPerPage);
        const startIdx = (currentPage - 1) * rowsPerPage;
        const endIdx = Math.min(startIdx + rowsPerPage, totalRows);
        const pageRows = rows.slice(startIdx, endIdx);

        // Build table HTML
        let html = '<div class="dataset-viewer">';
        html += `<h3>Dataset: ${escapeHtml(currentDataset)}</h3>`;
        html += `<div class="dataset-info">Showing rows ${startIdx + 1}-${endIdx} of ${totalRows} (retrieved up to 1,000 rows)</div>`;
        
        // Table
        html += '<div class="dataset-table-wrapper"><table class="dataset-table"><thead><tr>';
        columns.forEach((col, idx) => {
            const sortIndicator = currentSortColumn === idx 
                ? (currentSortAscending ? ' ▲' : ' ▼') 
                : '';
            html += `<th><a href="#" data-col-idx="${idx}">${escapeHtml(col)}${sortIndicator}</a></th>`;
        });
        html += '</tr></thead><tbody>';

        pageRows.forEach(row => {
            html += '<tr>';
            row.forEach(cell => {
                html += `<td>${escapeHtml(cell)}</td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table></div>';

        // Pagination controls
        html += '<div class="dataset-pagination">';
        if (currentPage > 1) {
            html += '<button class="btn-page" data-page="prev">Previous</button>';
        } else {
            html += '<button class="btn-page disabled" disabled>Previous</button>';
        }
        html += `<span class="page-indicator">Page ${currentPage} of ${totalPages}</span>`;
        if (currentPage < totalPages) {
            html += '<button class="btn-page" data-page="next">Next</button>';
        } else {
            html += '<button class="btn-page disabled" disabled>Next</button>';
        }
        html += '</div>';

        html += '</div>';

        showModal(html, false);

        // Attach event handlers
        const modal = document.getElementById("dataset-viewer-modal");
        
        // Column header click for sorting
        modal.querySelectorAll(".dataset-table th a").forEach(link => {
            link.addEventListener("click", function (e) {
                e.preventDefault();
                const colIdx = parseInt(this.getAttribute("data-col-idx"));
                
                if (currentSortColumn === colIdx) {
                    currentSortAscending = !currentSortAscending;
                } else {
                    currentSortColumn = colIdx;
                    currentSortAscending = true;
                }
                
                renderDatasetTable();
            });
        });

        // Pagination buttons
        modal.querySelectorAll(".btn-page").forEach(btn => {
            btn.addEventListener("click", function () {
                const page = this.getAttribute("data-page");
                if (page === "prev" && currentPage > 1) {
                    currentPage--;
                    renderDatasetTable();
                } else if (page === "next" && currentPage < totalPages) {
                    currentPage++;
                    renderDatasetTable();
                }
            });
        });
    }

    /**
     * Sorts rows by a specific column
     * Requirements: 8.6
     */
    function sortRows(rows, colIdx, ascending) {
        const sorted = [...rows].sort((a, b) => {
            const valA = a[colIdx] || "";
            const valB = b[colIdx] || "";
            
            // Try numeric comparison first
            const numA = parseFloat(valA);
            const numB = parseFloat(valB);
            
            if (!isNaN(numA) && !isNaN(numB)) {
                return ascending ? numA - numB : numB - numA;
            }
            
            // Fall back to string comparison
            return ascending 
                ? valA.localeCompare(valB) 
                : valB.localeCompare(valA);
        });
        
        return sorted;
    }

    /**
     * Shows the dataset viewer modal
     */
    function showModal(content, isLoading) {
        const modal = document.getElementById("dataset-viewer-modal");
        if (!modal) return;

        const contentDiv = modal.querySelector(".modal-content");
        if (contentDiv) {
            if (isLoading) {
                contentDiv.innerHTML = `<div class="modal-loading">${content}</div>`;
            } else {
                contentDiv.innerHTML = content;
            }
        }

        modal.classList.add("show");
    }

    /**
     * Closes the dataset viewer modal
     */
    function closeModal() {
        const modal = document.getElementById("dataset-viewer-modal");
        if (!modal) return;

        modal.classList.remove("show");

        // Clean up SignalR handlers if present
        if (window.currentFileBrowserJobId && window.currentFileBrowserHandlers) {
            const connection = window.logViewerHub?.connection;
            if (connection) {
                const handlers = window.currentFileBrowserHandlers;
                connection.off("ReceiveLog", handlers.receiveLog);
                connection.off("JobComplete", handlers.jobComplete);
                connection.off("JobError", handlers.jobError);
            }
            window.currentFileBrowserJobId = null;
            window.currentFileBrowserHandlers = null;
        }

        // Clear stored dataset data
        window.currentDatasetData = null;
        currentDataset = null;
        currentJobId = null;
    }

    /**
     * Escapes HTML to prevent XSS
     */
    function escapeHtml(text) {
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#039;'
        };
        return String(text).replace(/[&<>"']/g, m => map[m]);
    }

    // Initialize when DOM is ready
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    // Export for external access if needed
    window.fileBrowser = {
        refreshFileList,
        viewDataset
    };
})();

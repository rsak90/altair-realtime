/**
 * dataset-viewer.js
 * Advanced Dataset Viewer with filtering, sorting, and pagination
 */

(function () {
    let metadata = null;
    let currentPage = 1;
    let currentPageSize = 100;
    let currentSortColumn = null;
    let currentSortAscending = true;
    let currentWhereClause = '';

    /**
     * Initialize the dataset viewer
     */
    async function init() {
        await loadMetadata();
        await loadData();
        setupEventHandlers();
    }

    /**
     * Load dataset metadata
     */
    async function loadMetadata() {
        try {
            const response = await fetch(`/api/files/${encodeURIComponent(window.datasetName)}/metadata`);
            if (!response.ok) throw new Error("Failed to load metadata");
            
            metadata = await response.json();
            renderMetadataInfo();
        } catch (error) {
            console.error("Error loading metadata:", error);
            showError("Failed to load dataset metadata");
        }
    }

    /**
     * Load dataset data with current filters, sorting, and pagination
     */
    async function loadData() {
        try {
            showLoading();

            const filters = parseWhereClause(currentWhereClause);

            const request = {
                page: currentPage,
                pageSize: currentPageSize,
                sortColumn: currentSortColumn,
                sortAscending: currentSortAscending,
                filters: filters.length > 0 ? filters : null
            };

            const response = await fetch(`/api/files/${encodeURIComponent(window.datasetName)}/data`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });

            if (!response.ok) throw new Error("Failed to load data");
            
            const result = await response.json();
            renderTable(result);
            renderPagination(result);
        } catch (error) {
            console.error("Error loading data:", error);
            showError("Failed to load dataset data: " + error.message);
        }
    }

    /**
     * Render metadata information bar
     */
    function renderMetadataInfo() {
        if (!metadata) return;

        const infoDiv = document.getElementById("dataset-info");
        if (!infoDiv) return;

        const sizeKB = (metadata.fileSizeBytes / 1024).toFixed(1);
        const lastModified = new Date(metadata.lastModified).toLocaleString();

        infoDiv.innerHTML = `
            <span><strong>Rows:</strong> ${metadata.rowCount.toLocaleString()}</span>
            <span><strong>Columns:</strong> ${metadata.columnCount}</span>
            <span><strong>Size:</strong> ${sizeKB} KB</span>
            <span><strong>Modified:</strong> ${lastModified}</span>
        `;
    }

    /**
     * Render the data table
     */
    function renderTable(result) {
        const container = document.getElementById("table-container");
        if (!container) return;

        if (!result.items || result.items.length === 0) {
            container.innerHTML = '<div class="no-data">No data found matching the current filters</div>';
            return;
        }

        // Get column names from metadata
        const columns = metadata.columns.map(col => col.name);

        let html = '<table class="data-table"><thead><tr>';
        
        // Render column headers with sort controls
        columns.forEach(col => {
            const isSorted = currentSortColumn === col;
            const sortIcon = isSorted ? (currentSortAscending ? ' ▲' : ' ▼') : '';
            html += `<th>
                <a href="#" class="sort-header" data-column="${escapeHtml(col)}">
                    ${escapeHtml(col)}${sortIcon}
                </a>
            </th>`;
        });
        
        html += '</tr></thead><tbody>';

        // Render data rows
        result.items.forEach(row => {
            html += '<tr>';
            columns.forEach(col => {
                const value = row.columns[col] || '';
                html += `<td>${escapeHtml(value)}</td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table>';
        container.innerHTML = html;

        // Attach sort handlers
        container.querySelectorAll('.sort-header').forEach(header => {
            header.addEventListener('click', function (e) {
                e.preventDefault();
                const column = this.getAttribute('data-column');
                handleSort(column);
            });
        });
    }

    /**
     * Render pagination controls
     */
    function renderPagination(result) {
        const container = document.getElementById("pagination-container");
        if (!container) return;

        const totalPages = Math.ceil(result.totalCount / result.pageSize);
        const startRow = (result.page - 1) * result.pageSize + 1;
        const endRow = Math.min(result.page * result.pageSize, result.totalCount);

        let html = '<div class="pagination">';
        
        // Previous button
        if (result.page > 1) {
            html += '<button class="btn-page" data-page="1">First</button>';
            html += `<button class="btn-page" data-page="${result.page - 1}">Previous</button>`;
        } else {
            html += '<button class="btn-page disabled" disabled>First</button>';
            html += '<button class="btn-page disabled" disabled>Previous</button>';
        }

        // Page indicator
        html += `<span class="page-info">Rows ${startRow}-${endRow} of ${result.totalCount.toLocaleString()} | Page ${result.page} of ${totalPages}</span>`;

        // Page size selector
        html += '<select id="page-size-selector" class="page-size-select">';
        [50, 100, 200, 500, 1000].forEach(size => {
            const selected = size === currentPageSize ? ' selected' : '';
            html += `<option value="${size}"${selected}>${size} rows</option>`;
        });
        html += '</select>';

        // Next button
        if (result.page < totalPages) {
            html += `<button class="btn-page" data-page="${result.page + 1}">Next</button>`;
            html += `<button class="btn-page" data-page="${totalPages}">Last</button>`;
        } else {
            html += '<button class="btn-page disabled" disabled>Next</button>';
            html += '<button class="btn-page disabled" disabled>Last</button>';
        }

        html += '</div>';
        container.innerHTML = html;

        // Attach pagination handlers
        container.querySelectorAll('.btn-page:not(.disabled)').forEach(btn => {
            btn.addEventListener('click', function () {
                const page = parseInt(this.getAttribute('data-page'));
                currentPage = page;
                loadData();
            });
        });

        // Page size selector
        const sizeSelector = container.querySelector('#page-size-selector');
        if (sizeSelector) {
            sizeSelector.addEventListener('change', function () {
                currentPageSize = parseInt(this.value);
                currentPage = 1; // Reset to first page
                loadData();
            });
        }
    }

    /**
     * Handle column sort
     */
    function handleSort(column) {
        if (currentSortColumn === column) {
            currentSortAscending = !currentSortAscending;
        } else {
            currentSortColumn = column;
            currentSortAscending = true;
        }
        currentPage = 1; // Reset to first page
        loadData();
    }

    /**
     * Setup event handlers
     */
    function setupEventHandlers() {
        const btnApply = document.getElementById('btn-apply');
        if (btnApply) {
            btnApply.addEventListener('click', applyWhereClause);
        }

        const whereInput = document.getElementById('where-clause');
        if (whereInput) {
            whereInput.addEventListener('keypress', function(e) {
                if (e.key === 'Enter') {
                    applyWhereClause();
                }
            });
        }
    }

    /**
     * Apply WHERE clause filter
     */
    function applyWhereClause() {
        const whereInput = document.getElementById('where-clause');
        if (whereInput) {
            currentWhereClause = whereInput.value.trim();
            currentPage = 1; // Reset to first page
            loadData();
        }
    }

    /**
     * Parse WHERE clause into filter objects
     * Supports conditions like: age > 30, name CONTAINS 'John', status = 'Active'
     * Multiple conditions can be joined with AND
     */
    function parseWhereClause(whereClause) {
        if (!whereClause) return [];

        const filters = [];
        
        // Split by AND (case insensitive)
        const conditions = whereClause.split(/\s+AND\s+/i);

        conditions.forEach(condition => {
            condition = condition.trim();
            if (!condition) return;

            // Try to parse the condition
            // Patterns supported:
            // column operator value
            // Examples: age > 30, name = 'John', status CONTAINS 'Active'
            
            let match;
            
            // CONTAINS operator
            match = condition.match(/^(\w+)\s+CONTAINS\s+['"](.*?)['"]$/i);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'contains',
                    value: match[2]
                });
                return;
            }

            // STARTS WITH operator
            match = condition.match(/^(\w+)\s+STARTS\s+WITH\s+['"](.*?)['"]$/i);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'startswith',
                    value: match[2]
                });
                return;
            }

            // ENDS WITH operator
            match = condition.match(/^(\w+)\s+ENDS\s+WITH\s+['"](.*?)['"]$/i);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'endswith',
                    value: match[2]
                });
                return;
            }

            // >= operator
            match = condition.match(/^(\w+)\s*>=\s*(.+)$/);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'gte',
                    value: match[2].replace(/['"]/g, '')
                });
                return;
            }

            // <= operator
            match = condition.match(/^(\w+)\s*<=\s*(.+)$/);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'lte',
                    value: match[2].replace(/['"]/g, '')
                });
                return;
            }

            // > operator
            match = condition.match(/^(\w+)\s*>\s*(.+)$/);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'gt',
                    value: match[2].replace(/['"]/g, '')
                });
                return;
            }

            // < operator
            match = condition.match(/^(\w+)\s*<\s*(.+)$/);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'lt',
                    value: match[2].replace(/['"]/g, '')
                });
                return;
            }

            // = operator
            match = condition.match(/^(\w+)\s*=\s*(.+)$/);
            if (match) {
                filters.push({
                    columnName: match[1],
                    operator: 'equals',
                    value: match[2].replace(/['"]/g, '')
                });
                return;
            }

            console.warn('Could not parse condition:', condition);
        });

        return filters;
    }

    /**
     * Show loading indicator
     */
    function showLoading() {
        const container = document.getElementById("table-container");
        if (container) {
            container.innerHTML = '<div class="loading-indicator">Loading data...</div>';
        }
    }

    /**
     * Show error message
     */
    function showError(message) {
        const container = document.getElementById("table-container");
        if (container) {
            container.innerHTML = `<div class="error-message">${escapeHtml(message)}</div>`;
        }
    }

    /**
     * Escape HTML to prevent XSS
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
})();

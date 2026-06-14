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
    let currentFilters = [];

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

            const request = {
                page: currentPage,
                pageSize: currentPageSize,
                sortColumn: currentSortColumn,
                sortAscending: currentSortAscending,
                filters: currentFilters.length > 0 ? currentFilters : null
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
            showError("Failed to load dataset data");
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
        const btnAddFilter = document.getElementById('btn-add-filter');
        if (btnAddFilter) {
            btnAddFilter.addEventListener('click', addFilter);
        }

        const btnClearFilters = document.getElementById('btn-clear-filters');
        if (btnClearFilters) {
            btnClearFilters.addEventListener('click', clearFilters);
        }

        const btnRefresh = document.getElementById('btn-refresh');
        if (btnRefresh) {
            btnRefresh.addEventListener('click', () => loadData());
        }

        const btnExport = document.getElementById('btn-export');
        if (btnExport) {
            btnExport.addEventListener('click', exportToCSV);
        }
    }

    /**
     * Add a new filter row
     */
    function addFilter() {
        if (!metadata) return;

        const filterContainer = document.getElementById('filter-container');
        if (!filterContainer) return;

        const filterId = `filter-${Date.now()}`;
        const filterDiv = document.createElement('div');
        filterDiv.className = 'filter-row';
        filterDiv.id = filterId;

        let html = '<select class="filter-column">';
        metadata.columns.forEach(col => {
            html += `<option value="${escapeHtml(col.name)}">${escapeHtml(col.name)} (${col.type})</option>`;
        });
        html += '</select>';

        html += `<select class="filter-operator">
            <option value="equals">Equals</option>
            <option value="contains">Contains</option>
            <option value="startswith">Starts With</option>
            <option value="endswith">Ends With</option>
            <option value="gt">Greater Than</option>
            <option value="lt">Less Than</option>
            <option value="gte">Greater or Equal</option>
            <option value="lte">Less or Equal</option>
        </select>`;

        html += '<input type="text" class="filter-value" placeholder="Value">';
        html += '<button type="button" class="btn-apply-filter">Apply</button>';
        html += '<button type="button" class="btn-remove-filter">Remove</button>';

        filterDiv.innerHTML = html;
        filterContainer.appendChild(filterDiv);

        // Attach handlers
        filterDiv.querySelector('.btn-apply-filter').addEventListener('click', applyFilters);
        filterDiv.querySelector('.btn-remove-filter').addEventListener('click', function () {
            filterDiv.remove();
            applyFilters();
        });
    }

    /**
     * Apply all current filters
     */
    function applyFilters() {
        const filterContainer = document.getElementById('filter-container');
        if (!filterContainer) return;

        currentFilters = [];

        filterContainer.querySelectorAll('.filter-row').forEach(row => {
            const column = row.querySelector('.filter-column')?.value;
            const operator = row.querySelector('.filter-operator')?.value;
            const value = row.querySelector('.filter-value')?.value;

            if (column && operator && value) {
                currentFilters.push({
                    columnName: column,
                    operator: operator,
                    value: value
                });
            }
        });

        currentPage = 1; // Reset to first page
        loadData();
    }

    /**
     * Clear all filters
     */
    function clearFilters() {
        const filterContainer = document.getElementById('filter-container');
        if (filterContainer) {
            filterContainer.innerHTML = '';
        }
        currentFilters = [];
        currentPage = 1;
        loadData();
    }

    /**
     * Export current view to CSV
     */
    async function exportToCSV() {
        try {
            // Fetch all data with current filters (up to 10,000 rows)
            const request = {
                page: 1,
                pageSize: 10000,
                sortColumn: currentSortColumn,
                sortAscending: currentSortAscending,
                filters: currentFilters.length > 0 ? currentFilters : null
            };

            const response = await fetch(`/api/files/${encodeURIComponent(window.datasetName)}/data`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });

            if (!response.ok) throw new Error("Failed to fetch data for export");
            
            const result = await response.json();

            // Generate CSV
            const columns = metadata.columns.map(col => col.name);
            let csv = columns.map(col => `"${col}"`).join(',') + '\n';

            result.items.forEach(row => {
                const values = columns.map(col => {
                    const value = row.columns[col] || '';
                    return `"${String(value).replace(/"/g, '""')}"`;
                });
                csv += values.join(',') + '\n';
            });

            // Download
            const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = `${window.datasetName}_${new Date().toISOString().slice(0, 10)}.csv`;
            link.click();
        } catch (error) {
            console.error("Error exporting CSV:", error);
            alert("Failed to export CSV");
        }
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

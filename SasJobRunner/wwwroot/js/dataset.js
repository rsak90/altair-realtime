/**
 * dataset.js
 * Client-side sort and pagination for the Dataset Explorer.
 * Requirements: 7.3, 7.4
 *
 * The server renders an initial table; this script progressively enhances it
 * by wiring column-header clicks and prev/next buttons to fetch updated data
 * from /api/dataset without a full page reload.
 *
 * The dataset name is read from the URL query parameter "dataset" or from the
 * data-dataset attribute on #dataset-table (whichever is present).
 */

(function () {
    'use strict';

    // ── State ─────────────────────────────────────────────────────────────────

    let currentPage = 1;
    let currentSort = '';
    let currentDir  = 'asc';

    // ── Helpers ───────────────────────────────────────────────────────────────

    /**
     * Reads the active dataset name.
     * Priority: URL query param "dataset" → data-dataset on #dataset-table → body[data-dataset]
     * @returns {string}
     */
    function getDatasetName() {
        const params = new URLSearchParams(window.location.search);
        if (params.get('dataset')) return params.get('dataset');

        const table = document.getElementById('dataset-table');
        if (table && table.dataset.dataset) return table.dataset.dataset;

        if (document.body.dataset.dataset) return document.body.dataset.dataset;

        return '';
    }

    // ── Fetch and render ─────────────────────────────────────────────────────

    /**
     * Fetches a page of dataset rows from the API and re-renders the table body
     * and pagination controls.
     */
    async function fetchData() {
        const datasetName = getDatasetName();
        if (!datasetName) return;

        const url = `/api/dataset?datasetName=${encodeURIComponent(datasetName)}&page=${currentPage}&sort=${encodeURIComponent(currentSort)}&dir=${encodeURIComponent(currentDir)}`;

        try {
            const response = await fetch(url);
            if (!response.ok) {
                console.error('Dataset fetch failed:', response.status, await response.text());
                return;
            }

            const result = await response.json();
            renderRows(result.items ?? []);
            updatePagination(result.totalCount ?? 0, result.pageSize ?? 100);
        } catch (err) {
            console.error('Dataset fetch error:', err);
        }
    }

    /**
     * Re-renders <tbody> rows from an array of item objects.
     * Column order is derived from th[data-col] headers.
     * @param {Array<Object>} items
     */
    function renderRows(items) {
        const tbody = document.querySelector('.dataset-table tbody');
        if (!tbody) return;

        // Derive column order from header cells
        const headers = Array.from(document.querySelectorAll('th[data-col]'));
        const columnKeys = headers.map(th => th.getAttribute('data-col'));

        tbody.innerHTML = '';

        items.forEach(function (item) {
            const tr = document.createElement('tr');
            columnKeys.forEach(function (key) {
                const td = document.createElement('td');
                // Support both item.columns (DatasetRow model) and flat object
                const columns = item.columns || item;
                td.textContent = columns[key] != null ? columns[key] : '';
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        });
    }

    /**
     * Updates prev/next button states based on current page and total count.
     * @param {number} totalCount
     * @param {number} pageSize
     */
    function updatePagination(totalCount, pageSize) {
        const prevBtn = document.getElementById('prev-btn');
        const nextBtn = document.getElementById('next-btn');
        const totalPages = pageSize > 0 ? Math.ceil(totalCount / pageSize) : 1;

        if (prevBtn) {
            prevBtn.disabled = currentPage <= 1;
        }
        if (nextBtn) {
            nextBtn.disabled = currentPage >= totalPages;
        }
    }

    // ── Initialise ────────────────────────────────────────────────────────────

    document.addEventListener('DOMContentLoaded', function () {

        // ── Column header sort ───────────────────────────────────────────────

        document.querySelectorAll('th[data-col]').forEach(function (th) {
            th.style.cursor = 'pointer';
            th.addEventListener('click', function () {
                const col = th.getAttribute('data-col');
                if (currentSort === col) {
                    // Toggle direction on same column
                    currentDir = currentDir === 'asc' ? 'desc' : 'asc';
                } else {
                    currentSort = col;
                    currentDir  = 'asc';
                }
                currentPage = 1;
                fetchData();
            });
        });

        // ── Prev / Next pagination ───────────────────────────────────────────

        const prevBtn = document.getElementById('prev-btn');
        if (prevBtn) {
            prevBtn.addEventListener('click', function () {
                if (currentPage > 1) {
                    currentPage--;
                    fetchData();
                }
            });
        }

        const nextBtn = document.getElementById('next-btn');
        if (nextBtn) {
            nextBtn.addEventListener('click', function () {
                // fetchData checks total pages; increment optimistically
                currentPage++;
                fetchData();
            });
        }

        // If a dataset is already selected (page loaded with ?dataset=...), sync
        // the initial sort/page from the URL so state is consistent.
        const params = new URLSearchParams(window.location.search);
        if (params.get('sortColumn')) currentSort = params.get('sortColumn');
        if (params.get('sortDirection')) currentDir = params.get('sortDirection');
        if (params.get('page')) currentPage = parseInt(params.get('page'), 10) || 1;

        // Do NOT auto-fetch on load — the server has already rendered the initial
        // table. JS takes over only on user interaction.
    });

}());

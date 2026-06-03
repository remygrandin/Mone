// Mone chart helpers — wraps Chart.js v4 with two factory functions:
//   - createSparkline(canvasId, points, opts) — tiny inline trend chart, hover tooltip
//   - createTimeSeries(canvasId, points, opts) — full chart with axes/labels for detail view
//
// `points` is the wire shape [{ Timestamp, Value }, ...] returned by the metrics-series endpoint.
// `opts` may carry { unit?: string, displayName?: string, valueMapping?: { [number]: string } }.
//
// All charts created here are tracked in window.__moneCharts so we can destroy them when
// a Blazor component re-renders the same canvas with a fresh dataset.

(function () {
    if (typeof window === 'undefined') return;
    if (!window.Chart) {
        console.error('[Mone] Chart.js is not loaded; charts.js cannot run');
        return;
    }

    window.__moneCharts = window.__moneCharts || {};

    function destroyExisting(canvasId) {
        const existing = window.__moneCharts[canvasId];
        if (existing) {
            try { existing.destroy(); } catch (_) { /* canvas removed by Blazor */ }
            delete window.__moneCharts[canvasId];
        }
    }

    function formatValue(value, opts) {
        if (value === null || value === undefined || Number.isNaN(value)) return '—';
        const mapping = opts && opts.valueMapping;
        if (mapping && Object.prototype.hasOwnProperty.call(mapping, value)) {
            return mapping[value] + ' (' + value + ')';
        }
        const unit = opts && opts.unit;
        const numeric = Number.isInteger(value) ? value.toString() : value.toFixed(3);
        return unit ? numeric + ' ' + unit : numeric;
    }

    function formatTimestamp(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        if (Number.isNaN(d.getTime())) return iso;
        const pad = (n) => n.toString().padStart(2, '0');
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate())
            + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }

    function pointsToDataset(points) {
        // Chart.js needs labels (x-axis categories) + numeric data array.
        return {
            labels: points.map((p) => p.Timestamp),
            data: points.map((p) => p.Value),
        };
    }

    function commonInteraction(opts) {
        return {
            mode: 'index',
            intersect: false,
            callbacks: {
                title: (items) => items.length ? formatTimestamp(items[0].label) : '',
                label: (ctx) => formatValue(ctx.parsed.y, opts),
            },
        };
    }

    window.Mone = window.Mone || {};

    window.Mone.createSparkline = function (canvasId, points, opts) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return false;
        destroyExisting(canvasId);
        opts = opts || {};

        const ds = pointsToDataset(points || []);
        const chart = new window.Chart(canvas, {
            type: 'line',
            data: {
                labels: ds.labels,
                datasets: [{
                    data: ds.data,
                    borderColor: opts.color || 'rgba(94, 53, 177, 0.9)',
                    backgroundColor: 'rgba(94, 53, 177, 0.15)',
                    borderWidth: 1.5,
                    pointRadius: 0,
                    pointHoverRadius: 4,
                    pointHitRadius: 12,
                    pointHoverBorderColor: opts.color || 'rgba(94, 53, 177, 1)',
                    pointHoverBackgroundColor: '#fff',
                    tension: 0.15,
                    fill: true,
                    spanGaps: true,
                }],
            },
            options: {
                responsive: false,
                maintainAspectRatio: false,
                animation: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        enabled: true,
                        displayColors: false,
                        ...commonInteraction(opts),
                    },
                },
                scales: {
                    x: { display: false },
                    y: { display: false },
                },
                elements: { line: { borderJoinStyle: 'round' } },
            },
        });
        window.__moneCharts[canvasId] = chart;
        return true;
    };

    window.Mone.createTimeSeries = function (canvasId, points, opts) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return false;
        destroyExisting(canvasId);
        opts = opts || {};

        const ds = pointsToDataset(points || []);

        const yTickCallback = function (value) {
            const mapping = opts && opts.valueMapping;
            if (mapping && Object.prototype.hasOwnProperty.call(mapping, value)) {
                return mapping[value];
            }
            return value;
        };

        const chart = new window.Chart(canvas, {
            type: 'line',
            data: {
                labels: ds.labels,
                datasets: [{
                    label: opts.displayName || '',
                    data: ds.data,
                    borderColor: opts.color || 'rgba(94, 53, 177, 0.9)',
                    backgroundColor: 'rgba(94, 53, 177, 0.1)',
                    borderWidth: 1.5,
                    pointRadius: 2,
                    pointHoverRadius: 5,
                    pointHitRadius: 10,
                    tension: 0.1,
                    fill: true,
                    spanGaps: true,
                }],
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                plugins: {
                    legend: { display: !!opts.displayName },
                    tooltip: {
                        enabled: true,
                        displayColors: false,
                        ...commonInteraction(opts),
                    },
                },
                scales: {
                    x: {
                        display: true,
                        ticks: {
                            callback: function (val, idx) {
                                const label = this.getLabelForValue(val);
                                if (!label) return '';
                                const d = new Date(label);
                                if (Number.isNaN(d.getTime())) return '';
                                const pad = (n) => n.toString().padStart(2, '0');
                                return pad(d.getHours()) + ':' + pad(d.getMinutes());
                            },
                            maxTicksLimit: 8,
                            autoSkip: true,
                        },
                        grid: { display: false },
                    },
                    y: {
                        display: true,
                        ticks: { callback: yTickCallback, maxTicksLimit: 6 },
                        title: opts.unit ? { display: true, text: opts.unit } : { display: false },
                    },
                },
            },
        });
        window.__moneCharts[canvasId] = chart;
        return true;
    };

    window.Mone.destroyChart = function (canvasId) {
        destroyExisting(canvasId);
    };
})();

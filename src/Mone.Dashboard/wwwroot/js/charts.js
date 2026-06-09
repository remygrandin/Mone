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
        if (iso === null || iso === undefined || iso === '') return '';
        // Accept epoch-ms (number or all-digit string) as well as ISO strings.
        // ISO timestamps always contain non-digit chars, so an all-digit value
        // can only be epoch ms — new Date(numericString) would otherwise be NaN.
        const d = (typeof iso === 'number' || /^\d+$/.test(String(iso)))
            ? new Date(Number(iso))
            : new Date(iso);
        if (Number.isNaN(d.getTime())) return String(iso);
        const pad = (n) => n.toString().padStart(2, '0');
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate())
            + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }

    function pointsToDataset(points) {
        // Chart.js needs labels (x-axis categories) + numeric data array.
        // Blazor JS interop serializes C# properties as camelCase (timestamp/value);
        // the raw API JSON also uses camelCase. Accept PascalCase too for safety.
        return {
            labels: points.map((p) => p.timestamp ?? p.Timestamp),
            data: points.map((p) => p.value ?? p.Value),
        };
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, (c) => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
        }[c]));
    }

    // Sparklines render in very short containers (~28px), so Chart.js's canvas-drawn
    // tooltip gets clipped to that height. This renders the tooltip into a single shared
    // <div> appended to document.body — fixed-positioned and high z-index — so it floats
    // on top and is never clipped by the canvas. Returned as a Chart.js `external` handler.
    function getOrCreateTooltipEl() {
        let el = document.getElementById('mone-chart-tooltip');
        if (!el) {
            el = document.createElement('div');
            el.id = 'mone-chart-tooltip';
            el.style.cssText = 'position:fixed;pointer-events:none;z-index:3000;'
                + 'background:rgba(33,33,33,0.92);color:#fff;border-radius:4px;'
                + 'padding:4px 8px;font-size:11px;line-height:1.4;white-space:nowrap;'
                + 'transform:translate(-50%,calc(-100% - 8px));opacity:0;'
                + 'transition:opacity .1s ease;box-shadow:0 2px 8px rgba(0,0,0,0.3);';
            document.body.appendChild(el);
        }
        return el;
    }

    function externalTooltipHandler(context) {
        const el = getOrCreateTooltipEl();
        const tooltip = context.tooltip;
        if (!tooltip || tooltip.opacity === 0) {
            el.style.opacity = '0';
            return;
        }

        const lines = [];
        (tooltip.title || []).forEach((t) => {
            if (t) lines.push('<div style="opacity:0.7;font-size:10px;">' + escapeHtml(t) + '</div>');
        });
        (tooltip.body || []).forEach((b) => {
            (b.lines || []).forEach((l) => lines.push('<div>' + escapeHtml(l) + '</div>'));
        });
        el.innerHTML = lines.join('');

        const rect = context.chart.canvas.getBoundingClientRect();
        el.style.left = (rect.left + tooltip.caretX) + 'px';
        el.style.top = (rect.top + tooltip.caretY) + 'px';
        el.style.opacity = '1';
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

    // opts.fullWidth -> responsive width (canvas fills its parent; parent must have a size).
    // opts.background -> non-interactive backdrop variant: no tooltip, no hover points.
    window.Mone.createSparkline = function (canvasId, points, opts) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return false;
        destroyExisting(canvasId);
        opts = opts || {};
        const background = !!opts.background;

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
                    pointHoverRadius: background ? 0 : 4,
                    pointHitRadius: background ? 0 : 12,
                    pointHoverBorderColor: opts.color || 'rgba(94, 53, 177, 1)',
                    pointHoverBackgroundColor: '#fff',
                    tension: 0.15,
                    fill: true,
                    spanGaps: true,
                }],
            },
            options: {
                responsive: !!opts.fullWidth,
                maintainAspectRatio: false,
                animation: false,
                events: background ? [] : undefined,
                plugins: {
                    legend: { display: false },
                    tooltip: background
                        ? { enabled: false }
                        : { enabled: false, external: externalTooltipHandler, displayColors: false, ...commonInteraction(opts) },
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
                // responsive:true with no fixed-height parent makes Chart.js grow the
                // canvas every resize tick (infinite expansion). The canvas already has
                // explicit width/height attributes, so honor them instead.
                responsive: false,
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

    // Multi-series line chart for the Analysis tab. Each series may carry its own unit;
    // every distinct unit gets its own y-axis (y, y1, y2, y3, ...) alternating left/right,
    // so any number of differently-united metrics can be plotted together.
    // `opts.events` draws vertical reference lines at checker state-change times via an
    // inline afterDraw plugin (no annotation-plugin dependency).
    const PALETTE = [
        'rgba(94, 53, 177, 0.9)',   // purple
        'rgba(0, 137, 123, 0.9)',   // teal
        'rgba(216, 67, 21, 0.9)',   // deep orange
        'rgba(25, 118, 210, 0.9)',  // blue
        'rgba(124, 179, 66, 0.9)',  // light green
        'rgba(142, 36, 170, 0.9)',  // magenta
    ];

    function normPoints(points) {
        return (points || []).map((p) => ({
            ts: p.timestamp ?? p.Timestamp,
            value: p.value ?? p.Value,
        }));
    }

    // Bucket a timestamp to whole-second resolution (epoch ms floored to the second).
    // The chart's x-axis is a category scale keyed on these values, and the tooltip/ticks
    // only ever display down to the second — so two points from different datasets that
    // share the same displayed second must collapse onto one x-category to line up
    // vertically. Returns a number so labels sort numerically and Map keys compare by value.
    function bucketSecond(ts) {
        const ms = new Date(ts).getTime();
        return Number.isNaN(ms) ? ts : Math.floor(ms / 1000) * 1000;
    }

    window.Mone.createMultiSeries = function (canvasId, series, opts) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return false;
        destroyExisting(canvasId);
        opts = opts || {};
        series = series || [];

        // Union of all timestamps -> shared category labels (ascending). Per-series gaps
        // become nulls and are bridged with spanGaps so series on different cadences align.
        const labelSet = new Set();
        const normalized = series.map((s) => {
            const pts = normPoints(s.points);
            const map = new Map();
            pts.forEach((p) => {
                const key = bucketSecond(p.ts);
                labelSet.add(key);
                map.set(key, p.value); // within a series, last value in a second wins
            });
            return { meta: s, map };
        });
        const labels = Array.from(labelSet).sort((a, b) => a - b);

        // Distinct units in order of first appearance -> dedicated axis id.
        // The first unit owns 'y', every subsequent unit gets y1, y2, y3, ... so an
        // arbitrary number of differently-united series can each have a real scale.
        const units = [];
        normalized.forEach((s) => {
            const u = s.meta.unit || '';
            if (!units.includes(u)) units.push(u);
        });
        const unitToAxis = {};
        units.forEach((u, i) => { unitToAxis[u] = i === 0 ? 'y' : 'y' + i; });

        const datasets = normalized.map((s, i) => {
            const color = s.meta.color || PALETTE[i % PALETTE.length];
            const unit = s.meta.unit || '';
            const baseName = s.meta.displayName || s.meta.key || ('series ' + (i + 1));
            return {
                label: unit ? baseName + ' (' + unit + ')' : baseName,
                data: labels.map((l) => (s.map.has(l) ? s.map.get(l) : null)),
                borderColor: color,
                backgroundColor: color,
                borderWidth: 1.5,
                pointRadius: 0,
                pointHoverRadius: 4,
                pointHitRadius: 10,
                tension: 0.1,
                fill: false,
                spanGaps: true,
                yAxisID: unitToAxis[unit],
                _unit: unit,
            };
        });

        // Pre-compute label timestamps (ms) for event-line matching.
        const labelMs = labels.map((l) => new Date(l).getTime());
        const events = (opts.events || []).map((e) => ({
            ms: new Date(e.timestamp ?? e.Timestamp).getTime(),
            label: e.label ?? e.Label ?? '',
            color: e.color ?? e.Color ?? 'rgba(229, 57, 53, 0.7)',
        })).filter((e) => !Number.isNaN(e.ms));

        const eventLinePlugin = {
            id: 'moneEventLines',
            afterDraw(chart) {
                if (!events.length || !labelMs.length) return;
                const xScale = chart.scales.x;
                const area = chart.chartArea;
                const ctx = chart.ctx;
                ctx.save();
                events.forEach((ev) => {
                    // Nearest label index by absolute time distance.
                    let bestIdx = 0;
                    let bestDiff = Infinity;
                    for (let i = 0; i < labelMs.length; i++) {
                        const diff = Math.abs(labelMs[i] - ev.ms);
                        if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
                    }
                    const x = xScale.getPixelForValue(bestIdx);
                    if (x < area.left || x > area.right) return;
                    ctx.beginPath();
                    ctx.moveTo(x, area.top);
                    ctx.lineTo(x, area.bottom);
                    ctx.lineWidth = 1.5;
                    ctx.strokeStyle = ev.color;
                    ctx.setLineDash([4, 3]);
                    ctx.stroke();
                });
                ctx.restore();
            },
        };

        let sameDay = true;
        if (labelMs.length) {
            const first = new Date(labelMs[0]);
            const last = new Date(labelMs[labelMs.length - 1]);
            sameDay = first.getFullYear() === last.getFullYear()
                && first.getMonth() === last.getMonth()
                && first.getDate() === last.getDate();
        }

        const scales = {
            x: {
                display: true,
                ticks: {
                    callback: function (val) {
                        const label = this.getLabelForValue(val);
                        if (!label) return '';
                        const d = new Date(label);
                        if (Number.isNaN(d.getTime())) return '';
                        const pad = (n) => n.toString().padStart(2, '0');
                        const time = pad(d.getHours()) + ':' + pad(d.getMinutes());
                        if (sameDay) return time;
                        return d.getFullYear() + '/' + pad(d.getMonth() + 1) + '/' + pad(d.getDate()) + ' ' + time;
                    },
                    maxTicksLimit: 10,
                    autoSkip: true,
                },
                grid: { display: false },
            },
        };
        // One y-axis per distinct unit. Even-indexed units sit on the left, odd on the
        // right; only the first axis draws gridlines so the plot area stays readable.
        units.forEach((u, i) => {
            const axisId = i === 0 ? 'y' : 'y' + i;
            scales[axisId] = {
                display: true,
                position: i % 2 === 0 ? 'left' : 'right',
                title: u ? { display: true, text: u } : { display: false },
                ticks: { maxTicksLimit: 6 },
                grid: { drawOnChartArea: i === 0 },
            };
        });

        const chart = new window.Chart(canvas, {
            type: 'line',
            data: { labels: labels, datasets: datasets },
            options: {
                responsive: !!opts.fullWidth,
                maintainAspectRatio: false,
                animation: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: true, position: 'top' },
                    tooltip: {
                        enabled: true,
                        callbacks: {
                            title: (items) => items.length ? formatTimestamp(items[0].label) : '',
                            label: (ctx) => {
                                const v = ctx.parsed.y;
                                if (v === null || v === undefined) return '';
                                const u = ctx.dataset._unit;
                                return ctx.dataset.label + ': ' + formatValue(v, { unit: u });
                            },
                        },
                    },
                },
                scales: scales,
            },
            plugins: [eventLinePlugin],
        });
        window.__moneCharts[canvasId] = chart;
        return true;
    };

    window.Mone.destroyChart = function (canvasId) {
        destroyExisting(canvasId);
    };
})();

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

        // Pre-compute label timestamps (ms) for tick formatting.
        const labelMs = labels.map((l) => new Date(l).getTime());

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
        });
        window.__moneCharts[canvasId] = chart;
        return true;
    };

    window.Mone.destroyChart = function (canvasId) {
        destroyExisting(canvasId);
    };

    // --- Status strips ------------------------------------------------------
    // Horizontal status timelines drawn beneath the analysis chart: one strip for
    // the host's overall status and one per checker (collapsible). Every strip's
    // colored track is inset to the chart's plot area so it lines up with the graph,
    // and a single dashed "sync" bar follows the cursor across the chart and all
    // strips. Hovering a strip shows a plain-text tooltip with the timecode and the
    // status at that instant.
    window.__moneStrips = window.__moneStrips || {};

    function chartPlotArea(chartCanvasId) {
        const chart = window.__moneCharts[chartCanvasId];
        if (!chart || !chart.chartArea) return null;
        return { left: chart.chartArea.left, right: chart.chartArea.right };
    }

    function showStripTooltip(clientX, topY, text) {
        const el = getOrCreateTooltipEl();
        el.textContent = text;
        el.style.left = clientX + 'px';
        el.style.top = (topY + window.scrollY) + 'px';
        el.style.opacity = '1';
    }

    function hideStripTooltip() {
        const el = document.getElementById('mone-chart-tooltip');
        if (el) el.style.opacity = '0';
    }

    window.Mone.destroyStatusStrips = function (figureId) {
        const st = window.__moneStrips[figureId];
        if (st) {
            try { st.cleanup(); } catch (_) { /* nodes already gone */ }
            delete window.__moneStrips[figureId];
        }
    };

    window.Mone.createStatusStrips = function (figureId, stripsContainerId, chartCanvasId, payload) {
        const figure = document.getElementById(figureId);
        const container = document.getElementById(stripsContainerId);
        if (!figure || !container) return false;
        window.Mone.destroyStatusStrips(figureId);
        payload = payload || {};
        const dom = payload.domain || {};
        const startMs = Number(dom.startMs);
        const endMs = Number(dom.endMs);
        const span = Math.max(1, endMs - startMs);
        const sameDay = !!payload.sameDay;
        const host = payload.host || null;
        const checkers = payload.checkers || [];

        const pad = (n) => n.toString().padStart(2, '0');
        const fmtTick = (ms) => {
            const d = new Date(ms);
            const t = pad(d.getHours()) + ':' + pad(d.getMinutes());
            return sameDay ? t : (d.getFullYear() + '/' + pad(d.getMonth() + 1) + '/' + pad(d.getDate()) + ' ' + t);
        };
        const fmtFull = (ms) => {
            const d = new Date(ms);
            return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate())
                + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
        };
        const frac = (ms) => Math.min(1, Math.max(0, (ms - startMs) / span));

        const tracks = [];   // { trackEl, segments, label }
        const legends = [];  // legend row elements (ticks repositioned on layout)

        container.innerHTML = '';
        const wrap = document.createElement('div');
        wrap.style.cssText = 'position:relative;width:100%;font-size:11px;';
        container.appendChild(wrap);

        function addStrip(parent, label, segments, opts) {
            opts = opts || {};
            const block = document.createElement('div');
            block.style.cssText = 'margin:0 0 6px 0;';
            // caption (full width, above the track so long names stay readable)
            const cap = document.createElement('div');
            cap.style.cssText = 'display:flex;align-items:center;gap:6px;line-height:1.4;'
                + 'color:var(--mud-palette-text-secondary);font-weight:' + (opts.bold ? '600' : '400') + ';';
            const name = document.createElement('span');
            name.textContent = label;
            name.style.cssText = 'overflow:hidden;white-space:nowrap;text-overflow:ellipsis;';
            cap.appendChild(name);
            if (opts.currentColor) {
                const dot = document.createElement('span');
                dot.style.cssText = 'width:8px;height:8px;border-radius:50%;flex:0 0 auto;background:' + opts.currentColor + ';';
                cap.appendChild(dot);
                if (opts.currentLabel) {
                    const cl = document.createElement('span');
                    cl.textContent = opts.currentLabel;
                    cl.style.cssText = 'font-size:10px;';
                    cap.appendChild(cl);
                }
            }
            block.appendChild(cap);
            // track (inset to plot area in layout())
            const trackRow = document.createElement('div');
            trackRow.style.cssText = 'position:relative;height:' + (opts.height || 16) + 'px;';
            const track = document.createElement('div');
            track.style.cssText = 'position:absolute;top:0;height:100%;border-radius:3px;overflow:hidden;'
                + 'background:var(--mud-palette-background-grey);cursor:crosshair;';
            (segments || []).forEach((s) => {
                const l = frac(s.startMs) * 100;
                const r = frac(s.endMs) * 100;
                const seg = document.createElement('div');
                seg.style.cssText = 'position:absolute;top:0;height:100%;left:' + l + '%;width:'
                    + Math.max(0, r - l) + '%;background:' + (s.color || '#9e9e9e') + ';';
                track.appendChild(seg);
            });
            trackRow.appendChild(track);
            block.appendChild(trackRow);
            parent.appendChild(block);

            const entry = { track, segments: segments || [], label };
            tracks.push(entry);
            track.addEventListener('pointermove', (e) => onTrackHover(e, entry));
            track.addEventListener('pointerleave', onLeave);
            return entry;
        }

        function addLegend(parent) {
            const row = document.createElement('div');
            row.style.cssText = 'position:relative;height:16px;margin:2px 0 8px 0;color:var(--mud-palette-text-secondary);';
            const ticks = 6;
            for (let i = 0; i <= ticks; i++) {
                const sp = document.createElement('span');
                sp.dataset.frac = String(i / ticks);
                sp.textContent = fmtTick(startMs + (i / ticks) * span);
                sp.style.cssText = 'position:absolute;top:0;transform:translateX(-50%);white-space:nowrap;';
                row.appendChild(sp);
            }
            parent.appendChild(row);
            legends.push(row);
            return row;
        }

        // Host strip + its legend (always visible).
        if (host) {
            addStrip(wrap, host.label || 'Host', host.segments, {
                bold: true, height: 18, currentColor: host.currentColor, currentLabel: host.currentLabel,
            });
        }
        addLegend(wrap);

        // Collapsible checker section.
        if (checkers.length) {
            const toggle = document.createElement('button');
            toggle.type = 'button';
            let open = false;
            const arrow = () => (open ? '▾' : '▸');
            const setText = () => { toggle.textContent = arrow() + ' Checkers (' + checkers.length + ')'; };
            toggle.style.cssText = 'background:none;border:none;cursor:pointer;padding:2px 0;margin:0 0 4px 0;'
                + 'color:var(--mud-palette-primary);font-size:12px;font-weight:600;';
            setText();
            wrap.appendChild(toggle);

            const panel = document.createElement('div');
            panel.style.cssText = 'display:none;';
            wrap.appendChild(panel);

            checkers.forEach((c, i) => {
                addStrip(panel, c.label || ('Checker ' + (i + 1)), c.segments, {
                    height: 14, currentColor: c.currentColor, currentLabel: c.currentLabel,
                });
                // A legend every 5 checker strips so long lists stay readable.
                if ((i + 1) % 5 === 0 && i + 1 < checkers.length) addLegend(panel);
            });
            // Always close the section with a legend for the sub-strips.
            addLegend(panel);

            toggle.addEventListener('click', () => {
                open = !open;
                panel.style.display = open ? 'block' : 'none';
                setText();
                layout();
            });
        }

        // Shared sync bar spanning the chart + all strips.
        const sync = document.createElement('div');
        sync.style.cssText = 'position:absolute;top:0;width:0;pointer-events:none;opacity:0;z-index:5;'
            + 'border-left:1px dashed var(--mud-palette-text-primary);';
        figure.appendChild(sync);

        let plotLeft = 48;
        let plotRight = 0;
        function layout() {
            const plot = chartPlotArea(chartCanvasId);
            const cw = container.clientWidth || figure.clientWidth || 0;
            if (plot) { plotLeft = plot.left; plotRight = plot.right; }
            else { plotLeft = 48; plotRight = cw; }
            const w = Math.max(1, plotRight - plotLeft);
            tracks.forEach((t) => { t.track.style.left = plotLeft + 'px'; t.track.style.width = w + 'px'; });
            legends.forEach((lg) => {
                lg.querySelectorAll('span').forEach((sp) => {
                    sp.style.left = (plotLeft + parseFloat(sp.dataset.frac) * w) + 'px';
                });
            });
        }

        function placeSync(fr) {
            const w = Math.max(1, plotRight - plotLeft);
            sync.style.left = (plotLeft + fr * w) + 'px';
            sync.style.height = figure.clientHeight + 'px';
            sync.style.opacity = '0.55';
        }
        function hideSync() { sync.style.opacity = '0'; }

        function onTrackHover(e, entry) {
            const rect = entry.track.getBoundingClientRect();
            const fr = Math.min(1, Math.max(0, (e.clientX - rect.left) / Math.max(1, rect.width)));
            placeSync(fr);
            const ms = startMs + fr * span;
            const seg = entry.segments.find((s) => ms >= s.startMs && ms < s.endMs)
                || entry.segments[entry.segments.length - 1];
            const statusLabel = seg ? (seg.statusLabel || 'Unknown') : 'Unknown';
            showStripTooltip(e.clientX, rect.top, fmtFull(ms) + '  —  ' + entry.label + ': ' + statusLabel);
        }
        function onLeave() { hideSync(); hideStripTooltip(); }

        // Cursor over the chart drives the same sync bar.
        const canvas = document.getElementById(chartCanvasId);
        function onChartMove(e) {
            const plot = chartPlotArea(chartCanvasId);
            if (!plot) return;
            const rect = canvas.getBoundingClientRect();
            const x = e.clientX - rect.left;
            if (x < plot.left || x > plot.right) { hideSync(); return; }
            placeSync((x - plot.left) / Math.max(1, plot.right - plot.left));
        }
        if (canvas) {
            canvas.addEventListener('pointermove', onChartMove);
            canvas.addEventListener('pointerleave', hideSync);
        }

        const ro = ('ResizeObserver' in window) ? new ResizeObserver(() => layout()) : null;
        if (ro) ro.observe(container);

        // The chart renders on its own async cycle; wait for its plot area before first layout.
        let tries = 0;
        (function ready() {
            if (chartPlotArea(chartCanvasId) || tries > 30) { layout(); return; }
            tries++;
            requestAnimationFrame(ready);
        })();

        window.__moneStrips[figureId] = {
            cleanup() {
                if (ro) ro.disconnect();
                if (canvas) {
                    canvas.removeEventListener('pointermove', onChartMove);
                    canvas.removeEventListener('pointerleave', hideSync);
                }
                if (sync.parentNode) sync.parentNode.removeChild(sync);
                container.innerHTML = '';
                hideStripTooltip();
            },
        };
        return true;
    };
})();

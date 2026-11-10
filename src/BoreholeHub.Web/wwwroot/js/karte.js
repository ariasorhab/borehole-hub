// Project map: OpenStreetMap base layer, optional HLNUG overlays (GK 25 via ArcGIS export, GÜK 300 and BFD 50 via WMS), markers for all Aufschlüsse.
(function () {
    'use strict';

    var root = document.getElementById('karte');
    if (!root || typeof L === 'undefined') { return; }

    // Texts come from data-t-* attributes rendered by Karte.cshtml (docs/i18n.md, rule (e)); the German is the fallback.
    function T(name, fallback) {
        var value = root.getAttribute('data-t-' + name);
        return value === null ? fallback : value;
    }

    // German number formats: one decimal for depths and heights, no decimals and no grouping for coordinates.
    var nf1 = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    var nf0 = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 0, useGrouping: false });

    var map = L.map('karte-map', { zoomControl: true }).setView([50.917, 9.2317], 16);

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        className: 'karte-basemap', // lets the dark theme darken the base map only (see site.css)
        attribution: T('osm', '&copy; OpenStreetMap-Mitwirkende')
    }).addTo(map);

    var GEOLOGY_MAX_ZOOM = 14;

    // Overlays are off by default; the checkboxes in the side panel toggle them.
    var wmsOptions = { format: 'image/png', transparent: true, version: '1.3.0', opacity: 0.6, attribution: '&copy; HLNUG',
        // 512 px tiles: four times fewer requests to the (slow) ArcGIS WMS; zoomOffset keeps the map scale the same.
        tileSize: 512, zoomOffset: -1 };
    var overlays = {
        'layer-geologie': L.tileLayer.wms(
            'https://geodienste-umwelt.hessen.de/arcgis/services/inspire/geologie/MapServer/WMSServer',
            Object.assign({ layers: 'GUEK300_Geologie' }, wmsOptions)),
        'layer-boden': L.tileLayer.wms(
            'https://geodienste-umwelt.hessen.de/arcgis/services/boden/bfd50/MapServer/WMSServer',
            Object.assign({ layers: 'BFD50_Bodenhauptgruppen' }, wmsOptions))
    };
    // ---- GK 25 (HLNUG, 1:25 000): ArcGIS REST MapServer, one image per map view, refreshed after moveend ----
    // Layers 21 (boundaries) and 22 (Kartiereinheiten); the WMS of this service does not answer, the REST "export" does.
    var GK25_URL = 'https://geodienste-umwelt.hessen.de/arcgis/rest/services/geologie/gk25/MapServer';
    var GK25_OPACITY = 0.6;
    var gk25 = L.imageOverlay('data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==',
        [[0, 0], [0, 0]], { opacity: GK25_OPACITY, attribution: '&copy; HLNUG', interactive: false });
    var gk25Timer = null;
    var gk25Request = 0;
    var gk25Popup = null;

    function gk25Refresh() {
        if (!map.hasLayer(gk25)) { return; }
        var size = map.getSize();
        var b = map.getBounds();
        var sw = L.CRS.EPSG3857.project(b.getSouthWest());
        var ne = L.CRS.EPSG3857.project(b.getNorthEast());
        var url = GK25_URL + '/export?f=image&format=png32&transparent=true&layers=show:21,22'
            + '&bbox=' + [sw.x, sw.y, ne.x, ne.y].join(',') + '&bboxSR=3857&imageSR=3857'
            + '&size=' + size.x + ',' + size.y;
        var ticket = ++gk25Request;
        var img = new Image();
        img.onload = function () {
            // ignore answers for an older map view
            if (ticket !== gk25Request || !map.hasLayer(gk25)) { return; }
            gk25.setUrl(url);
            gk25.setBounds(b);
        };
        img.src = url;
    }

    function gk25Schedule() {
        window.clearTimeout(gk25Timer);
        gk25Timer = window.setTimeout(gk25Refresh, 250);
    }
    map.on('moveend', gk25Schedule);

    // Click identify: unit name of the GK 25 map unit at the clicked point (only while the layer is on).
    map.on('click', function (e) {
        if (!map.hasLayer(gk25)) { return; }
        var size = map.getSize();
        var b = map.getBounds();
        var sw = L.CRS.EPSG3857.project(b.getSouthWest());
        var ne = L.CRS.EPSG3857.project(b.getNorthEast());
        var pt = L.CRS.EPSG3857.project(e.latlng);
        var url = GK25_URL + '/identify?f=json&layers=all:22&tolerance=2&returnGeometry=false&sr=3857'
            + '&geometryType=esriGeometryPoint&geometry=' + pt.x + ',' + pt.y
            + '&mapExtent=' + [sw.x, sw.y, ne.x, ne.y].join(',') + '&imageDisplay=' + size.x + ',' + size.y + ',96';
        fetch(url)
            .then(function (r) { if (!r.ok) { throw new Error('HTTP ' + r.status); } return r.json(); })
            .then(function (data) {
                var hit = data.results && data.results[0];
                var box = el('div', 'karte-popup');
                if (!hit) {
                    box.appendChild(el('strong', 'karte-popup-title', T('gk25', 'Geologie (GK 25)')));
                    box.appendChild(el('p', 'karte-popup-note', T('no-unit', 'Hier ist keine Kartiereinheit verzeichnet.')));
                } else {
                    var a = hit.attributes;
                    box.appendChild(el('strong', 'karte-popup-title', a['Kartiereinheit'] || T('unit', 'Kartiereinheit')));
                    var lines = [a['Kürzel'] ? T('code', 'Kürzel') + ': ' + a['Kürzel'] : null, a['Stratigraphische Zuordnung'], a['Hauptgesteinseinheit']];
                    lines.forEach(function (t) { if (t && t !== 'Null') { box.appendChild(el('p', 'karte-popup-note', t)); } });
                    box.appendChild(el('p', 'karte-popup-note', 'GK 25, \u00a9 HLNUG'));
                }
                gk25Popup = L.popup({ maxWidth: 260 }).setLatLng(e.latlng).setContent(box).openOn(map);
            })
            .catch(function () { /* identify is optional: no popup if the service does not answer */ });
    });

    var overlays = {
        'layer-gk25': gk25,
        'layer-geologie': overlays['layer-geologie'],
        'layer-boden': overlays['layer-boden']
    };
    var defaultOpacity = { 'layer-gk25': GK25_OPACITY, 'layer-geologie': 0.6, 'layer-boden': 0.6 };

    Object.keys(overlays).forEach(function (id) {
        var box = document.getElementById(id);
        if (!box) { return; }
        var slider = document.getElementById('opacity-' + id);
        var sliderRow = slider ? slider.closest('.karte-opacity') : null;
        var output = sliderRow ? sliderRow.querySelector('output') : null;
        box.checked = false;
        if (slider) {
            slider.value = String(Math.round(defaultOpacity[id] * 100));
            slider.addEventListener('input', function () {
                overlays[id].setOpacity(slider.value / 100);
                if (output) { output.textContent = slider.value + ' %'; }
            });
        }
        box.addEventListener('change', function () {
            if (sliderRow) { sliderRow.hidden = !box.checked; }
            if (!box.checked) {
                if (id === 'layer-gk25' && gk25Popup) { map.closePopup(gk25Popup); gk25Popup = null; }
                map.removeLayer(overlays[id]);
                return;
            }
            // The GÜK 300 service draws nothing at scales larger than about 1:22 000: zoom out first, then add the
            // layer, so no tile requests are started (and aborted) at the old zoom level.
            if (id === 'layer-geologie' && map.getZoom() > GEOLOGY_MAX_ZOOM) {
                map.once('moveend', function () { if (box.checked) { overlays[id].addTo(map); } });
                map.flyTo(map.getCenter(), GEOLOGY_MAX_ZOOM, { duration: 0.6 });
            } else {
                overlays[id].addTo(map);
                if (id === 'layer-gk25') { gk25Refresh(); }
            }
        });
    });

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) { node.className = className; }
        if (text !== undefined) { node.textContent = text; }
        return node;
    }

    function icon(p) {
        var isKrb = p.art === 'Kleinrammbohrung';
        var cls = 'mk ' + (isKrb ? 'mk-krb' : 'mk-dph') + (p.grundwasserM !== null ? ' has-gw' : '');
        return L.divIcon({ className: 'karte-icon', html: '<span class="' + cls + '"></span>', iconSize: [20, 20], iconAnchor: [10, 10], popupAnchor: [0, -12] });
    }

    // Kleinrammbohrung / Rammsondierung are the enum names from the server; the popup shows their translated labels.
    function artLabel(art) {
        if (art === 'Kleinrammbohrung') { return T('krb', art); }
        if (art === 'Rammsondierung') { return T('dph', art); }
        return art;
    }

    function popupContent(p) {
        var box = el('div', 'karte-popup');
        box.appendChild(el('strong', 'karte-popup-title', p.bezeichnung));
        var rows = [
            [T('type', 'Art'), artLabel(p.art)],
            [T('depth', 'Endteufe'), nf1.format(p.endteufeM) + ' m'],
            [T('start', 'Ansatzhöhe'), nf1.format(p.ansatzhoeheMNhn) + ' m NHN'],
            [T('groundwater', 'Grundwasser'), p.grundwasserM !== null ? nf1.format(p.grundwasserM) + ' ' + T('below', 'm u. GOK') : T('none', 'nicht angetroffen')],
            [T('easting', 'Rechtswert'), nf0.format(p.rechtswert)],
            [T('northing', 'Hochwert'), nf0.format(p.hochwert)]
        ];
        var dl = el('dl', 'karte-popup-list');
        rows.forEach(function (r) {
            dl.appendChild(el('dt', null, r[0]));
            dl.appendChild(el('dd', null, r[1]));
        });
        box.appendChild(dl);
        box.appendChild(el('p', 'karte-popup-note', 'EPSG:25832'));
        var actions = el('div', 'karte-popup-actions');
        actions.style.marginTop = 'var(--s3)';
        var link = el('a', 'btn btn-primary btn-sm', T('profile', 'Profil öffnen'));
        link.href = '/Aufschluesse/' + p.id;
        actions.appendChild(link);
        box.appendChild(actions);
        return box;
    }

    var markers = {};

    // Permanent labels only when zoomed in (zoom >= 17); popups work at every zoom level.
    var LABEL_MIN_ZOOM = 17;
    function updateLabels() {
        var show = map.getZoom() >= LABEL_MIN_ZOOM;
        Object.keys(markers).forEach(function (id) {
            if (show) { markers[id].openTooltip(); } else { markers[id].closeTooltip(); }
        });
    }
    map.on('zoomend', updateLabels);

    fetch(root.getAttribute('data-punkte-url'), { headers: { 'Accept': 'application/json' } })
        .then(function (response) {
            if (!response.ok) { throw new Error('HTTP ' + response.status); }
            return response.json();
        })
        .then(function (collection) {
            var bounds = [];
            collection.features.forEach(function (f) {
                var p = f.properties;
                var latlng = [f.geometry.coordinates[1], f.geometry.coordinates[0]];
                var marker = L.marker(latlng, { icon: icon(p), title: p.bezeichnung, riseOnHover: true }).addTo(map);
                // A DPH stands about 2 m from its KRB: label KRB below and DPH above so the labels do not cover each other.
                var below = p.art === 'Kleinrammbohrung';
                marker.bindTooltip(p.bezeichnung, { permanent: true, direction: below ? 'bottom' : 'top', offset: [0, below ? 10 : -10], className: 'karte-label' });
                marker.bindPopup(popupContent(p), { closeButton: true, autoPanPadding: [24, 24] });
                markers[p.id] = marker;
                bounds.push(latlng);
            });
            if (bounds.length > 0) {
                map.fitBounds(bounds, { padding: [64, 64], maxZoom: 19 });
            }
            updateLabels();
        })
        .catch(function () {
            var notice = el('div', 'notice karte-error', T('load-error', 'Die Punkte konnten nicht geladen werden.'));
            root.querySelector('.karte-panel').prepend(notice);
        });

    // A click on a list entry flies to the marker and opens its popup.
    var items = root.querySelectorAll('.karte-item');
    items.forEach(function (item) {
        item.addEventListener('click', function () {
            var marker = markers[item.getAttribute('data-id')];
            if (!marker) { return; }
            items.forEach(function (other) { other.classList.toggle('is-active', other === item); });
            var target = marker.getLatLng();
            var zoom = Math.max(map.getZoom(), 19);
            if (map.getZoom() === zoom && map.getCenter().distanceTo(target) < 0.5) {
                marker.openPopup(); // already there: no movement, so no moveend event would follow
                return;
            }
            map.once('moveend', function () { marker.openPopup(); });
            map.flyTo(target, zoom, { duration: 0.7 });
        });
    });
}());

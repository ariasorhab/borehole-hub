// Project map: OpenStreetMap base layer, optional HLNUG WMS overlays, markers for all Aufschlüsse.
(function () {
    'use strict';

    var root = document.getElementById('karte');
    if (!root || typeof L === 'undefined') { return; }

    // German number formats: one decimal for depths and heights, no decimals and no grouping for coordinates.
    var nf1 = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    var nf0 = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 0, useGrouping: false });

    var map = L.map('karte-map', { zoomControl: true }).setView([50.917, 9.2317], 16);

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        className: 'karte-basemap', // lets the dark theme darken the base map only (see site.css)
        attribution: '&copy; OpenStreetMap-Mitwirkende'
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
    Object.keys(overlays).forEach(function (id) {
        var box = document.getElementById(id);
        if (!box) { return; }
        box.checked = false;
        box.addEventListener('change', function () {
            if (!box.checked) {
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

    function popupContent(p) {
        var box = el('div', 'karte-popup');
        box.appendChild(el('strong', 'karte-popup-title', p.bezeichnung));
        var rows = [
            ['Art', p.art],
            ['Endteufe', nf1.format(p.endteufeM) + ' m'],
            ['Ansatzhöhe', nf1.format(p.ansatzhoeheMNhn) + ' m NHN'],
            ['Grundwasser', p.grundwasserM !== null ? nf1.format(p.grundwasserM) + ' m u. GOK' : 'nicht angetroffen'],
            ['Rechtswert', nf0.format(p.rechtswert)],
            ['Hochwert', nf0.format(p.hochwert)]
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
        var link = el('a', 'btn btn-primary btn-sm', 'Profil öffnen');
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
            var notice = el('div', 'notice karte-error', 'Die Punkte konnten nicht geladen werden.');
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

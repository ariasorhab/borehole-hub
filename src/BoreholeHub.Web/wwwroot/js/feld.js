/* Feldaufnahme: art toggle, layer rows, N10 grid, "Standort übernehmen". No libraries, no inline handlers. */
(function () {
    "use strict";

    var form = document.getElementById("feld-form");
    if (!form) { return; }

    var bezeichnung = document.getElementById("bezeichnung");
    var endteufe = document.getElementById("endteufe");
    var sectionKrb = document.getElementById("section-krb");
    var sectionDph = document.getElementById("section-dph");
    var schichten = document.getElementById("schichten");
    var template = document.getElementById("schicht-template");
    var addButton = document.getElementById("schicht-add");
    var n10 = document.getElementById("n10");
    var standortButton = document.getElementById("standort-btn");
    var standortStatus = document.getElementById("standort-status");

    var maxSteps = parseInt(n10 ? n10.getAttribute("data-max") : "100", 10) || 100;
    var standardSteps = parseInt(n10 ? n10.getAttribute("data-standard") : "40", 10) || 40;

    var initialSchichtenHtml = schichten ? schichten.innerHTML : "";
    var initialN10Html = n10 ? n10.innerHTML : "";
    var n10Store = {};          // typed N10 values by step, kept while the grid is rebuilt
    var bezeichnungEdited = false;

    // ---------- helpers ----------

    function currentArt() {
        var checked = form.querySelector('input[name="Art"]:checked');
        return checked ? checked.value : "KRB";
    }

    function prefillFor(art) {
        return bezeichnung.getAttribute(art === "DPH" ? "data-prefill-dph" : "data-prefill-krb") || "";
    }

    function parseNumber(text) {
        var t = String(text || "").trim();
        if (t === "" || (t.indexOf(",") >= 0 && t.indexOf(".") >= 0)) { return NaN; }
        var n = Number(t.replace(",", "."));
        return isFinite(n) ? n : NaN;
    }

    function formatComma(value, digits) {
        return value.toFixed(digits).replace(".", ",");
    }

    // ---------- Art: show one section, swap the Bezeichnung prefill ----------

    function syncArt() {
        var art = currentArt();
        if (sectionKrb) { sectionKrb.hidden = art !== "KRB"; }
        if (sectionDph) { sectionDph.hidden = art !== "DPH"; }
    }

    function onArtChange() {
        syncArt();
        if (!bezeichnungEdited) { bezeichnung.value = prefillFor(currentArt()); }
    }

    function initBezeichnung() {
        var value = bezeichnung.value.trim();
        bezeichnungEdited = value !== "" && value !== prefillFor(currentArt());
    }

    // ---------- Layer rows ----------

    function rows() {
        return schichten ? Array.prototype.slice.call(schichten.querySelectorAll("[data-row]")) : [];
    }

    function renumber() {
        rows().forEach(function (row, i) {
            row.id = "schicht-" + i;
            var nr = row.querySelector("[data-nr]");
            if (nr) { nr.textContent = String(i + 1); }
            Array.prototype.forEach.call(row.querySelectorAll("[name], [id], [for]"), function (el) {
                if (el.name) { el.name = el.name.replace(/^Schichten\[[^\]]*\]/, "Schichten[" + i + "]"); }
                if (el.id) { el.id = el.id.replace(/^schicht-[^-]+-/, "schicht-" + i + "-"); }
                if (el.htmlFor) { el.htmlFor = el.htmlFor.replace(/^schicht-[^-]+-/, "schicht-" + i + "-"); }
            });
        });
    }

    function addRow() {
        if (!schichten || !template) { return; }
        var existing = rows();
        var previousBis = existing.length ? existing[existing.length - 1].querySelector('input[name$=".Bis"]') : null;
        var holder = document.createElement("div");
        holder.innerHTML = template.innerHTML.replace(/__i__/g, String(existing.length));
        var row = holder.querySelector("[data-row]");
        if (!row) { return; }
        schichten.appendChild(row);
        var von = row.querySelector('input[name$=".Von"]');
        if (von && previousBis && previousBis.value.trim() !== "") {
            von.value = previousBis.value.trim();
            von.setAttribute("data-auto-von", von.value);
        }
        renumber();
        var first = row.querySelector("input, select");
        if (first) { first.focus(); }
    }

    function removeRow(row) {
        row.parentNode.removeChild(row);
        if (rows().length === 0) { addRow(); } else { renumber(); }
    }

    // D74: the next row's Von follows this row's Bis while it is empty or still holds the value written here before.
    function syncNextVon(row, bis) {
        var list = rows();
        var next = list[list.indexOf(row) + 1];
        if (!next) { return; }
        var von = next.querySelector('input[name$=".Von"]');
        if (!von) { return; }
        var auto = von.getAttribute("data-auto-von");
        if (von.value.trim() !== "" && von.value !== auto) { return; }   // the user typed their own Von
        var value = bis.value.trim();
        von.value = value;
        von.setAttribute("data-auto-von", value);
    }

    // A row that only has the prefilled Von counts as empty: clear it so the server ignores it.
    function clearUntouchedRows() {
        rows().forEach(function (row) {
            var others = Array.prototype.some.call(row.querySelectorAll("input, select"), function (el) {
                return !/\.Von$/.test(el.name) && el.value.trim() !== "";
            });
            if (!others) {
                var von = row.querySelector('input[name$=".Von"]');
                if (von) { von.value = ""; }
            }
        });
    }

    // ---------- N10 grid ----------

    function stepsFromEndteufe() {
        var e = parseNumber(endteufe ? endteufe.value : "");
        if (!(e > 0)) { return standardSteps; }
        return Math.min(maxSteps, Math.max(1, Math.ceil(e * 10 - 1e-9)));
    }

    function currentSteps() {
        return n10 ? n10.querySelectorAll(".n10-cells input").length : 0;
    }

    function storeN10() {
        if (!n10) { return; }
        Array.prototype.forEach.call(n10.querySelectorAll(".n10-cells input"), function (input) {
            var step = parseInt(input.name.replace(/^N10\[(\d+)\]$/, "$1"), 10);
            if (step) { n10Store[step] = input.value; }
        });
    }

    function buildN10(steps) {
        if (!n10) { return; }
        storeN10();
        Array.prototype.forEach.call(n10.querySelectorAll("[data-n10-row]"), function (row) { row.parentNode.removeChild(row); });
        for (var r = 0; r * 10 < steps; r++) {
            var row = document.createElement("div");
            row.className = "n10-row";
            row.setAttribute("data-n10-row", "");
            var label = document.createElement("span");
            label.className = "n10-label";
            label.textContent = r + "–" + (r + 1) + " m";
            var cells = document.createElement("span");
            cells.className = "n10-cells";
            for (var c = 1; c <= 10 && r * 10 + c <= steps; c++) {
                var step = r * 10 + c;
                var input = document.createElement("input");
                input.type = "text";
                input.setAttribute("inputmode", "numeric");
                input.setAttribute("autocomplete", "off");
                input.maxLength = 3;
                input.id = "n10-" + step;
                input.name = "N10[" + step + "]";
                input.setAttribute("aria-label", "N10 bis " + formatComma(step / 10, 2) + " m");
                input.setAttribute("aria-invalid", "false");
                if (n10Store[step]) { input.value = n10Store[step]; }
                cells.appendChild(input);
            }
            row.appendChild(label);
            row.appendChild(cells);
            n10.appendChild(row);
        }
    }

    function onEndteufeChange() {
        var steps = stepsFromEndteufe();
        if (steps !== currentSteps()) { buildN10(steps); }
    }

    // ---------- Standort übernehmen ----------

    function standortMessage(text, isError) {
        if (!standortStatus) { return; }
        standortStatus.textContent = text;
        standortStatus.classList.toggle("is-error", !!isError);
    }

    var NICHT_VERFUEGBAR = "Standort nicht verfügbar – bitte Koordinaten eingeben.";

    function takeLocation() {
        if (!navigator.geolocation) { standortMessage(NICHT_VERFUEGBAR, true); return; }
        standortButton.disabled = true;
        standortMessage("Standort wird ermittelt …", false);

        function done() { standortButton.disabled = false; }

        navigator.geolocation.getCurrentPosition(function (pos) {
            var lat = pos.coords.latitude.toFixed(7);
            var lon = pos.coords.longitude.toFixed(7);
            var accuracy = Math.round(pos.coords.accuracy || 0);
            var url = form.getAttribute("data-utm-url") + "&lat=" + encodeURIComponent(lat) + "&lon=" + encodeURIComponent(lon);
            fetch(url, { credentials: "same-origin", headers: { "Accept": "application/json" } })
                .then(function (res) {
                    if (!res.ok) { throw new Error("http " + res.status); }
                    return res.json();
                })
                .then(function (data) {
                    document.getElementById("rechtswert").value = formatComma(Number(data.rechtswert), 1);
                    document.getElementById("hochwert").value = formatComma(Number(data.hochwert), 1);
                    standortMessage("Genauigkeit ± " + accuracy + " m", false);
                    done();
                })
                .catch(function () { standortMessage(NICHT_VERFUEGBAR, true); done(); });
        }, function () {
            standortMessage(NICHT_VERFUEGBAR, true);
            done();
        }, { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 });
    }

    // ---------- wiring ----------

    Array.prototype.forEach.call(form.querySelectorAll('input[name="Art"]'), function (radio) {
        radio.addEventListener("change", onArtChange);
    });

    bezeichnung.addEventListener("input", function () { bezeichnungEdited = true; });

    if (endteufe) {
        endteufe.addEventListener("input", onEndteufeChange);
        endteufe.addEventListener("change", onEndteufeChange);
    }

    if (addButton) { addButton.addEventListener("click", addRow); }

    if (schichten) {
        schichten.addEventListener("input", function (event) {
            var target = event.target;
            if (!target || !/\.Bis$/.test(target.name || "")) { return; }
            var row = target.closest ? target.closest("[data-row]") : null;
            if (row) { syncNextVon(row, target); }
        });

        schichten.addEventListener("click", function (event) {
            var button = event.target.closest ? event.target.closest('[data-action="remove"]') : null;
            if (!button) { return; }
            var row = button.closest("[data-row]");
            if (row) { removeRow(row); }
        });
    }

    if (standortButton) { standortButton.addEventListener("click", takeLocation); }

    form.addEventListener("submit", function () {
        renumber();
        clearUntouchedRows();
    });

    form.addEventListener("reset", function () {
        // The browser resets the values after this event; then restore the rows and the grid as the server rendered them.
        window.setTimeout(function () {
            if (schichten) { schichten.innerHTML = initialSchichtenHtml; }
            if (n10) { n10.innerHTML = initialN10Html; }
            n10Store = {};
            standortMessage("", false);
            syncArt();
            initBezeichnung();
            if (endteufe && stepsFromEndteufe() !== currentSteps()) { buildN10(stepsFromEndteufe()); }
        }, 0);
    });

    initBezeichnung();
    syncArt();
    if (endteufe && stepsFromEndteufe() !== currentSteps()) { buildN10(stepsFromEndteufe()); }

    var fehlerbox = document.getElementById("fehlerbox");
    if (fehlerbox) { fehlerbox.focus(); }
}());

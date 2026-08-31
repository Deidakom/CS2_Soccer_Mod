import { Entity, Instance } from "cs_script/point_script";

let menuLayout = null;

function getMenuLayout() {
    if (!(menuLayout instanceof Entity) || !menuLayout.IsValid()) {
        menuLayout = Instance.FindEntitiesByName("sm2_classic_menu_layout")[0];
    }
    return menuLayout;
}

function decodeHexUtf8(value) {
    if (!value || value === "-") {
        return "";
    }

    let escaped = "";
    for (let index = 0; index + 1 < value.length; index += 2) {
        escaped += `%${value.substring(index, index + 2)}`;
    }

    try {
        return decodeURIComponent(escaped);
    } catch (_) {
        return "";
    }
}

function setVariable(layout, playerSlot, name, value) {
    layout.SetDialogVariableStringForPlayer(playerSlot, "menu", name, value);
}

function setEmpty(layout, playerSlot, panelId, isEmpty) {
    layout.SetHasClassForPlayer(playerSlot, panelId, "Empty", isEmpty);
}

function parsePlayerSlot(value) {
    const playerSlot = Number(value);
    return Number.isInteger(playerSlot) && playerSlot >= 0 && playerSlot < 64
        ? playerSlot
        : -1;
}

function beginMenu(parts) {
    if (parts.length < 5) {
        return;
    }

    const layout = getMenuLayout();
    if (!(layout instanceof Entity) || !layout.IsValid()) {
        return;
    }

    const playerSlot = parsePlayerSlot(parts[1]);
    if (playerSlot < 0) {
        return;
    }
    const pageNumber = Number(parts[2]);
    const totalPages = Number(parts[3]);
    layout.SetHasClassForPlayer(playerSlot, "menu", "Hidden", true);
    setVariable(layout, playerSlot, "title", decodeHexUtf8(parts[4]));

    const pageText = totalPages > 1 ? `${pageNumber} / ${totalPages}` : "";
    setVariable(layout, playerSlot, "page", pageText);
    setEmpty(layout, playerSlot, "page", pageText.length === 0);

    for (let line = 1; line <= 9; line++) {
        setVariable(layout, playerSlot, `line${line}`, "");
        setEmpty(layout, playerSlot, `line_${line}`, true);
    }
}

function setLine(parts) {
    if (parts.length < 4) {
        return;
    }

    const layout = getMenuLayout();
    if (!(layout instanceof Entity) || !layout.IsValid()) {
        return;
    }

    const playerSlot = parsePlayerSlot(parts[1]);
    const line = Number(parts[2]);
    if (playerSlot < 0 || !Number.isInteger(line) || line < 1 || line > 9) {
        return;
    }

    const text = decodeHexUtf8(parts[3]);
    setVariable(layout, playerSlot, `line${line}`, text);
    setEmpty(layout, playerSlot, `line_${line}`, text.length === 0);
}

function showMenu(parts) {
    if (parts.length < 2) {
        return;
    }

    const layout = getMenuLayout();
    if (!(layout instanceof Entity) || !layout.IsValid()) {
        return;
    }

    const playerSlot = parsePlayerSlot(parts[1]);
    if (playerSlot < 0) {
        return;
    }
    layout.SetInputCaptureEnabled(playerSlot, false);
    layout.SetHasClassForPlayer(playerSlot, "menu", "Hidden", false);
}

function hideMenu(parts) {
    if (parts.length < 2) {
        return;
    }

    const layout = getMenuLayout();
    if (!(layout instanceof Entity) || !layout.IsValid()) {
        return;
    }

    const playerSlot = parsePlayerSlot(parts[1]);
    if (playerSlot < 0) {
        return;
    }
    layout.SetInputCaptureEnabled(playerSlot, false);
    layout.SetHasClassForPlayer(playerSlot, "menu", "Hidden", true);
}

Instance.OnScriptInput("Apply", (inputData) => {
    const payload = inputData.caller?.GetEntityName();
    if (!payload || !payload.startsWith("sm2h|")) {
        return;
    }

    const parts = payload.substring(5).split("|");
    if (parts[0] === "begin") {
        beginMenu(parts);
    } else if (parts[0] === "line") {
        setLine(parts);
    } else if (parts[0] === "show") {
        showMenu(parts);
    } else if (parts[0] === "close") {
        hideMenu(parts);
    }
});

Instance.OnScriptInput("ReadyProbe", () => {
    Instance.ServerCommand("css_sm2menu_classic_ready");
});

// CounterStrikeSharp uses this acknowledgement to decide whether it is safe
// to stop drawing the plain fallback. Missing/mis-mounted addon = no signal.
Instance.ServerCommand("css_sm2menu_classic_ready");

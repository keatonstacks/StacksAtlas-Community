
import { Chip, Tooltip } from "@mui/material";
import {
    Smartphone,
    Tv,
    Router,
    Print,
    Computer,
    Kitchen,
    VideogameAsset,
    Security,
    Hub,
    DevicesOther,
    Headset,
    SpeakerGroup,
    SettingsInputHdmi,
    Storage,
    Tablet,
    Mic,
    VolumeUp,
    Tune,
    Videocam,
} from "@mui/icons-material";
import type { ReactElement } from "react";

export const getPortServiceName = (port: number): string => {
    const serviceMap: Record<number, string> = {
        44400: "Dante Control",
        4440: "Dante Aux",
        8800: "Dante Config",
        41794: "Crestron CIP",
        41795: "Crestron CTP",
        41796: "Crestron SCIP",
        41797: "Crestron Secure",
        1702: "Q-SYS Control",
        1700: "Q-SYS Core",
        1704: "Q-SYS Stream",
        1710: "Q-SYS Remote",
        1317: "Biamp Tesira",
        2202: "Biamp TTP",
        1319: "AMX Control",
        8008: "Google Cast (HTTP)",
        8009: "Google Cast (Secure)",
        62078: "Apple Lockdown",
        32400: "Plex Server",
        1883: "MQTT Broker",
        5353: "mDNS/Bonjour",
        22: "SSH",
        23: "Telnet/Console",
        80: "HTTP (Web)",
        443: "HTTPS (Secure)",
        631: "IPP (Printer)",
        9100: "RAW Printing",
        3389: "RDP (Remote Desktop)",
        554: "RTSP (Video Stream)",
        5900: "VNC",
        8000: "Shure Control",
        8080: "HTTP Alt",
        8443: "HTTPS Alt"
    };

    return serviceMap[port] || `Port ${port}`;
};

/** Normalize API / manual type strings for icon lookup (handles legacy camelCase labels). */
export function normalizeDeviceType(type: string | null | undefined): string {
    return (type || "unknown")
        .replace(/_/g, " ")
        .replace(/([a-z])([A-Z])/g, "$1 $2")
        .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
        .toLowerCase()
        .replace(/\s+/g, " ")
        .trim();
}

export type DeviceIconKind =
    | "tv"
    | "camera"
    | "audio"
    | "mic"
    | "amp"
    | "mixer"
    | "control"
    | "conference"
    | "mobile"
    | "tablet"
    | "gaming"
    | "network"
    | "storage"
    | "computer"
    | "printer"
    | "security"
    | "iot"
    | "hub"
    | "generic";

/** Map a device type label to an icon category (aligned with DEVICE_TYPE_OPTIONS). */
export function getDeviceIconKind(
    type: string | null | undefined,
    model?: string | null,
    name?: string | null,
): DeviceIconKind {
    const t = normalizeDeviceType(type);
    const combined = `${(model || "").toLowerCase()} ${(name || "").toLowerCase()}`;

    if (t.startsWith("video display") || t.startsWith("video projector") || t === "media player") return "tv";
    if (t.startsWith("video camera") || t.startsWith("video ptz")) return "camera";
    if (t.startsWith("video")) return "tv";

    if (t.startsWith("audio microphone")) return "mic";
    if (t.startsWith("audio amplifier") || t.startsWith("audio av receiver") || t.startsWith("audio soundbar")) return "amp";
    if (t.startsWith("audio console")) return "mixer";
    if (t.startsWith("audio dsp") || t.startsWith("audio speaker") || t.startsWith("audio endpoint")
        || t.startsWith("audio interface") || t.startsWith("audio dante")) return "audio";

    if (t.startsWith("control") || t.startsWith("lighting")) return "control";
    if (t.startsWith("collaboration")) return "conference";

    if (t === "mobile" || t.startsWith("phone")) return "mobile";
    if (t === "tablet") return "tablet";
    if (t === "gaming console") return "gaming";

    if (t.startsWith("network") || t.startsWith("power")) return "network";
    if (t.startsWith("storage") || t === "server") return "storage";
    if (t === "computer" || t === "laptop" || t === "workstation" || t === "virtual machine") return "computer";
    if (t === "printer") return "printer";
    if (t === "iot") return "iot";
    if (t.startsWith("digital signage") || t.startsWith("streaming")) return "tv";

    // Legacy / heuristic fallbacks when type is still Unknown
    if (combined.includes("tv") || combined.includes("oled") || combined.includes("display")) return "tv";
    if (t.includes("control")) return "control";
    if (t.includes("confer") || t.includes("collabor")) return "conference";
    if (t.includes("audio") || t.includes("dsp") || t.includes("speaker") || t.includes("sonos")) {
        if (combined.includes("mic") || combined.includes("mxw") || combined.includes("mxa")) return "mic";
        if (combined.includes("amp") || combined.includes("crown")) return "amp";
        if (combined.includes("mixer") || combined.includes("console")) return "mixer";
        return "audio";
    }
    if (t.includes("mobile") || t.includes("iphone") || t.includes("phone")) return "mobile";
    if (t.includes("tablet") || t.includes("ipad")) return "tablet";
    if (t.includes("media") || t.includes("chromecast")) return "tv";
    if (t.includes("gaming") || t.includes("xbox") || t.includes("playstation")) return "gaming";
    if (t.includes("network") || t.includes("router") || t.includes("switch")) return "network";
    if (t.includes("infrastructure") || t.includes("server") || t.includes("nas")) return "storage";
    if (t.includes("computer") || t.includes("pc") || t.includes("mac") || t.includes("laptop")) return "computer";
    if (t.includes("printer")) return "printer";
    if (t.includes("security") || t.includes("camera")) return "security";
    if (t.includes("iot") || t.includes("smart home")) return "iot";
    if (t.includes("hub")) return "hub";

    return t === "unknown" || t === "custom" ? "generic" : "generic";
}

const ICON_BY_KIND: Record<DeviceIconKind, ReactElement> = {
    tv: <Tv fontSize="small" />,
    camera: <Videocam fontSize="small" />,
    audio: <SpeakerGroup fontSize="small" />,
    mic: <Mic fontSize="small" />,
    amp: <VolumeUp fontSize="small" />,
    mixer: <Tune fontSize="small" />,
    control: <SettingsInputHdmi fontSize="small" />,
    conference: <Headset fontSize="small" />,
    mobile: <Smartphone fontSize="small" />,
    tablet: <Tablet fontSize="small" />,
    gaming: <VideogameAsset fontSize="small" />,
    network: <Router fontSize="small" />,
    storage: <Storage fontSize="small" />,
    computer: <Computer fontSize="small" />,
    printer: <Print fontSize="small" />,
    security: <Security fontSize="small" />,
    iot: <Kitchen fontSize="small" />,
    hub: <Hub fontSize="small" />,
    generic: <DevicesOther fontSize="small" sx={{ opacity: 0.55 }} />,
};

export const getDeviceIcon = (
    type: string | null | undefined,
    model?: string | null | undefined,
    name?: string | null | undefined,
) => ICON_BY_KIND[getDeviceIconKind(type, model, name)];

/**
 * Checks if a device is Dante-enabled based on its model, type, vendor, or open ports
 */
export const isDanteDevice = (device: {
    model?: string | null;
    type?: string | null;
    vendor?: string | null;
    openPorts?: number[] | null;
}): boolean => {
    const model = (device.model || "").toLowerCase();
    const type = (device.type || "").toLowerCase();
    const vendor = (device.vendor || "").toLowerCase();
    const ports = device.openPorts || [];

    if (ports.includes(44400) || ports.includes(4440) || ports.includes(8800)) {
        return true;
    }

    const danteKeywords = ["dante", "rio", "ulxd", "qlxd", "mxw", "mxa", "rednet", "audinate"];
    if (danteKeywords.some(kw => model.includes(kw) || type.includes(kw))) {
        return true;
    }

    const danteVendors = ["audinate", "yamaha", "shure", "focusrite"];
    if (danteVendors.some(v => vendor.includes(v)) &&
        (type.includes("audio") || model.includes("audio") || model.includes("rio") || model.includes("rednet"))) {
        return true;
    }

    return false;
};

export const DanteBadge = () => (
    <Tooltip title="Dante-Enabled Device (Audio-over-IP)" arrow>
        <Chip
            label="DANTE"
            size="small"
            sx={{
                ml: 1,
                height: 18,
                fontSize: '0.65rem',
                fontWeight: 800,
                letterSpacing: '0.05em',
                backgroundColor: 'rgba(0, 150, 136, 0.15)',
                color: '#00BFA5',
                border: '1px solid rgba(0, 150, 136, 0.3)',
                '& .MuiChip-label': { px: 0.75 }
            }}
        />
    </Tooltip>
);

#!/usr/bin/env python3
# 与 src/Bluetooth.cs 中公开纯逻辑保持对照的本地测试（不需要 Windows / csc）。

def normalize_address_key(text):
    if not text:
        return ""
    hexpart = "".join(c.upper() for c in text if c in "0123456789abcdefABCDEF")
    if len(hexpart) > 12:
        hexpart = hexpart[-12:]
    return hexpart

def parse_address(text):
    key = normalize_address_key(text)
    if len(key) != 12:
        return 0
    try:
        return int(key, 16)
    except ValueError:
        return 0

def format_address(address):
    s = format(address, "X")
    s = s.zfill(12)
    if len(s) > 12:
        s = s[-12:]
    return ":".join(s[i:i+2] for i in range(0, 12, 2))

def address_tag(address):
    s = format(address, "X").zfill(12)
    if len(s) > 12:
        s = s[-12:]
    return s[8:12]

def is_valid_mac_hex12(s):
    if not s or len(s) != 12:
        return False
    if s in ("000000000000", "FFFFFFFFFFFF", "00805F9B34FB"):
        return False
    return all(c in "0123456789ABCDEFabcdef" for c in s)

def extract_bluetooth_address_from_instance_id(instance_id):
    if not instance_id:
        return 0
    stripped = []
    in_braces = False
    for ch in instance_id:
        if ch == "{":
            in_braces = True
            stripped.append("_")
            continue
        if ch == "}":
            in_braces = False
            stripped.append("_")
            continue
        if not in_braces:
            stripped.append(ch.upper())
    s = "".join(stripped)

    dev_idx = s.find("DEV_")
    if dev_idx >= 0 and dev_idx + 16 <= len(s):
        cand = s[dev_idx + 4:dev_idx + 16]
        if is_valid_mac_hex12(cand):
            return parse_address(cand)

    bt_idx = s.find("BLUETOOTHDEVICE_")
    if bt_idx >= 0 and bt_idx + 28 <= len(s):
        cand = s[bt_idx + 16:bt_idx + 28]
        if is_valid_mac_hex12(cand):
            return parse_address(cand)

    import re
    for tok in re.split(r"[\\&_#\-\:]", s):
        if len(tok) == 12 and is_valid_mac_hex12(tok):
            return parse_address(tok)
    return 0

def crc16_xmodem(data):
    crc = 0
    for b in data:
        crc ^= (b << 8) & 0xFFFF
        for _ in range(8):
            if crc & 0x8000:
                crc = ((crc << 1) ^ 0x1021) & 0xFFFF
            else:
                crc = (crc << 1) & 0xFFFF
    return crc

def build_huawei_5a_frame(service_id, command_id, payload=b""):
    payload = payload or b""
    data_len = len(payload) + 3
    body = bytearray([0x5A, (data_len >> 8) & 0xFF, data_len & 0xFF, 0x00, service_id, command_id])
    body.extend(payload)
    crc = crc16_xmodem(body)
    body.extend([(crc >> 8) & 0xFF, crc & 0xFF])
    return bytes(body)

def try_parse_huawei_5a_battery(buf):
    if not buf or len(buf) < 8:
        return False, -1, -1, -1, -1
    i = 0
    while i + 8 <= len(buf):
        if buf[i] != 0x5A:
            i += 1
            continue
        length = (buf[i + 1] << 8) | buf[i + 2]
        frame_len = length + 5
        if frame_len < 8 or i + frame_len > len(buf):
            return False, -1, -1, -1, -1
        cmd = (buf[i + 4] << 8) | buf[i + 5]
        if cmd in (0x0127, 0x0108):
            p = i + 6
            end = i + frame_len - 2
            overall = left = right = case_pct = -1
            while p + 1 < end:
                t = buf[p]
                tlen = buf[p + 1]
                vp = p + 2
                if vp + tlen > end:
                    break
                if t == 1 and tlen >= 1 and 0 <= buf[vp] <= 100:
                    overall = buf[vp]
                elif t == 2 and tlen >= 3:
                    if 0 <= buf[vp] <= 100:
                        left = buf[vp]
                    if 0 <= buf[vp + 1] <= 100:
                        right = buf[vp + 1]
                    if 0 <= buf[vp + 2] <= 100:
                        case_pct = buf[vp + 2]
                p = vp + tlen
            if overall >= 0 or left >= 0 or right >= 0:
                if overall < 0:
                    if left > 0 and right > 0:
                        overall = (left + right) // 2
                    elif left >= 0:
                        overall = left
                    elif right >= 0:
                        overall = right
                return overall >= 0, overall, left, right, case_pct
        i += frame_len
    return False, -1, -1, -1, -1

def try_read_battery_percent_value(raw):
    if raw is None:
        return False, -1
    try:
        v = int(raw)
    except (TypeError, ValueError):
        return False, -1
    if 0 <= v <= 100:
        return True, v
    return False, -1

def status_text(status):
    return {
        "Querying": "读取中",
        "Fresh": "",
        "Stale": "历史",
        "NoSystemValue": "系统未提供",
        "Unmatched": "未匹配",
        "Timeout": "超时",
        "AccessDenied": "无权限",
        "Failed": "失败",
        "None": "未上报",
    }.get(status, "未上报")

def category_looks_audio(hint):
    if not hint:
        return False
    h = hint.lower()
    return any(t in h for t in ("audio", "headphone", "headset", "earbud", "speaker", "耳机", "音箱"))

def category_looks_input(hint):
    if not hint:
        return False
    h = hint.lower()
    return any(t in h for t in ("input", "keyboard", "mouse", "hid", "gaming", "键盘", "鼠标"))

def looks_stale(has_battery, status, sampled_age_minutes, fresh_minutes=30):
    if not has_battery:
        return False
    if status == "Stale":
        return True
    if status == "Fresh":
        return False
    if sampled_age_minutes is None:
        return True
    return sampled_age_minutes >= fresh_minutes

def backoff_seconds(fail_count):
    if fail_count <= 0:
        return 20
    if fail_count == 1:
        return 45
    if fail_count == 2:
        return 120
    return 300

def run():
    # address
    assert normalize_address_key("AA:BB:CC:DD:EE:FF") == "AABBCCDDEEFF"
    assert normalize_address_key("aa-bb-cc-dd-ee-ff") == "AABBCCDDEEFF"
    assert parse_address("AA:BB:CC:DD:EE:FF") == 0xAABBCCDDEEFF
    assert parse_address("zz") == 0
    assert parse_address("AABBCC") == 0  # incomplete, do not guess
    assert format_address(0xAABBCCDDEEFF) == "AA:BB:CC:DD:EE:FF"
    assert address_tag(0xAABBCCDDEEFF) == "EEFF"
    assert "AABBCCDD" not in address_tag(0xAABBCCDDEEFF)

    # PnP DeviceInstanceId extraction (Classic, HFP service, BLE root, BLE GATT service)
    assert extract_bluetooth_address_from_instance_id(
        r"BTHENUM\DEV_D46A6A1235C1\8&2A1B3C4D&0&BLUETOOTHDEVICE_D46A6A1235C1"
    ) == 0xD46A6A1235C1
    assert extract_bluetooth_address_from_instance_id(
        r"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_LOCALMFG&0002\8&2A1B3C4D&0&D46A6A1235C1_C00000000"
    ) == 0xD46A6A1235C1
    assert extract_bluetooth_address_from_instance_id(
        r"BTHLE\DEV_E8D03C9A1B2E\8&2A1B3C4D&0&E8D03C9A1B2E"
    ) == 0xE8D03C9A1B2E
    assert extract_bluetooth_address_from_instance_id(
        r"BTHLEDEVICE\{0000180f-0000-1000-8000-00805f9b34fb}_DEV_VID&02046D_PID&B034_REV&0012_E8D03C9A1B2E\9&11223344&0&0019"
    ) == 0xE8D03C9A1B2E
    # Ensure Bluetooth base UUID suffix 00805F9B34FB is never mistaken for a MAC address
    assert extract_bluetooth_address_from_instance_id(
        r"BTHLEDEVICE\{0000180f-0000-1000-8000-00805f9b34fb}\9&11223344&0&0019"
    ) == 0

    # Huawei 5A frame building & parsing
    f_simple = build_huawei_5a_frame(0x01, 0x08)
    assert f_simple == bytes([0x5A, 0x00, 0x03, 0x00, 0x01, 0x08, 0xDF, 0x73])
    resp_frame = build_huawei_5a_frame(0x01, 0x08, bytes([0x01, 0x01, 82, 0x02, 0x03, 85, 80, 65]))
    ok, ov, l, r, c = try_parse_huawei_5a_battery(resp_frame)
    assert ok and ov == 82 and l == 85 and r == 80 and c == 65

    # battery property: official 101 = unknown, never 101%
    assert try_read_battery_percent_value(85) == (True, 85)
    assert try_read_battery_percent_value(0) == (True, 0)
    assert try_read_battery_percent_value(100) == (True, 100)
    assert try_read_battery_percent_value(101) == (False, -1)
    assert try_read_battery_percent_value(None) == (False, -1)
    assert try_read_battery_percent_value(255) == (False, -1)

    assert status_text("NoSystemValue") == "系统未提供"
    assert status_text("Timeout") == "超时"
    assert status_text("Fresh") == ""

    assert category_looks_audio("Audio.Headphone")
    assert category_looks_input("Input.Keyboard")
    assert not category_looks_audio("Display.Monitor")

    assert looks_stale(True, "Stale", 1) is True
    assert looks_stale(True, "Fresh", 10) is False
    assert looks_stale(True, "Failed", 40) is True
    assert looks_stale(True, "Failed", None) is True
    assert looks_stale(False, "NoSystemValue", None) is False

    assert backoff_seconds(0) == 20
    assert backoff_seconds(3) == 300
    print("ok", "all battery logic checks passed")

if __name__ == "__main__":
    run()

# Pixel Buds Pro 2 Protocol Notes

What the buds expect over their control channel, captured from mypixelbuds.google.com on 2026-10-10. `capture.log` holds the raw bytes; `decode.py` turns them into readable calls:

```bash
python decode.py capture.log
```

## Transport

- Bluetooth RFCOMM, service UUID `25e97ff7-24ce-4c4c-8951-f764a708f7b5`.
- Same "Maestro" protocol as the original Pixel Buds Pro. See [pbpctrl](https://github.com/qzed/pbpctrl) (MIT/Apache-2.0) for the full message definitions in `libmaestro/proto/maestro_pw.proto`.
- Framing: HDLC U-frames (`7e` … `7e`, `7d` escapes, CRC-32 at the end), carrying pigweed RPC packets with protobuf payloads.
- Service and method IDs are the 65599 hash of their names (`maestro_pw.Maestro` → `0x7ede71ea`).
- The buds serve one pair of channels per session: 24/26 in the first capture, 19/21 in a later one (left/right bud each). The app asks all four at once and keeps whichever answers first, like pbpctrl.
- Frames to the buds are addressed host (1) → left bud (3) or right bud (4). Replies come from the bud to Maestro A or B (10 or 13).
- Right after the link opens the buds announce their software info with call ID 0xFFFFFFFF. It isn't a reply to anything; real replies echo the request's call ID.

## Startup Sequence

1. `SubscribeRuntimeInfo` (battery and in-case status, streamed)
2. `SubscribeToSettingsChanges` (setting changes made on the buds, streamed)
3. `GetHardwareInfo`, `GetSoftwareInfo`
4. `ReadSetting` for each setting below

## Settings (`WriteSetting` → `SettingValue` field)

| UI control | Field | Value |
| --- | --- | --- |
| Noise control mode | 13 | 1 Off, 2 Noise Cancellation, 3 Transparency, 4 Adaptive |
| Conversation Detection | 22 | bool |
| Multipoint | 11 | bool |
| Equalizer | 16 | `EqBands` floats -6 to 6, step 0.5: 1 Low Bass, 2 Bass, 3 Mid, 4 Treble, 5 Upper Treble |
| Use touch controls | 4 | bool |
| Use head gestures | 29 | enum: 1 off, 2 on (new on Pro 2, not in pbpctrl) |
| Press and hold, left / right | 7 | `GestureControl` left = 1, right = 2; value 5 Toggle Active Noise Control, 6 Digital assistant |
| Noise modes cycled by press and hold | 12 | `AncrGestureLoop` bools: 1 Noise Cancellation, 2 Off, 3 Transparency, 4 Adaptive |

The web app also reads settings 26, 27 and 28 at startup. What they are is unknown, and the UI doesn't need them.

## Equalizer Presets

Presets live in the web app, not on the buds. Picking one just writes these bands (Low Bass, Bass, Mid, Treble, Upper Treble):

| Preset | Bands |
| --- | --- |
| Default | 0, 0, 0, 0, 0 |
| Heavy bass | 5, 3, 0, 0, 0 |
| Light bass | -5, -1.5, 0, 0, 0 |
| Balanced | -3.5, 0.5, 1, -1, 2.5 |
| Vocal boost | -1, 0, 4, 2, 0 |
| Clarity | -2, 0, 2, 3, 5 |

## Replies

Confirmed on real buds (firmware release_6.144) from both the web app and MagicBuds:

- `ReadSetting` answers with `SettingsRsp` carrying the same `SettingValue` field, e.g. `22 02 68 03` = noise mode Transparency, `22 03 e8 01 02` = head gestures on.
- `SubscribeRuntimeInfo` streams `RuntimeInfo`: battery per bud (the case only while a bud sits in it) and placement.
- Turn on Debug logging in Settings > General to log every packet.

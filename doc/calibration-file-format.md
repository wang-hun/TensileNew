# DICE Calibration File Format

The calibration workspace exports UTF-8 JSON with schema identifier `dice-calibration` and version `2`. Consumers should check both fields and ignore unknown fields they do not use.

```json
{
  "schema": "dice-calibration",
  "schemaVersion": 2,
  "m": 20,
  "mUnit": "pixel/mm",
  "M10": 10,
  "M10Unit": "mm",
  "alpha": 60,
  "alphaUnit": "deg",
  "F": 16,
  "FUnit": "mm",
  "s": 2.74,
  "sUnit": "um/pixel",
  "H": 291.9708029,
  "HUnit": "mm",
  "HFormula": "((F * 1000) / s) / m"
}
```

| Field | Meaning | Unit / rule |
|---|---|---|
| `m` | Image pixel scale | `pixel/mm`; computed as measured reference pixel length divided by `M10`. |
| `M10` | Known reference-circle diameter | `mm`; editable, defaults to 10. |
| `alpha` | Horizontal angle | `deg`; editable, defaults to 60. |
| `F` | Physical lens focal length | `mm`; editable, defaults to 16. |
| `s` | Sensor pixel pitch | `um/pixel`; editable, defaults to 2.74. |
| `H` | Optical-center height | `mm`; derived as `f / m`, where `f = F * 1000 / s`. |
| `HFormula` | Machine-readable derivation | `((F * 1000) / s) / m`; the factor 1000 converts millimeters to micrometers. |

The calibration parameters persisted in the current user's local application data are `m`, `M10`, `alpha`, `F`, and `s`. The pixel focal length `f` is an intermediate value only: it is calculated in memory, is marked ignored by JSON serialization, and is intentionally absent from this export format. `H` is a derived result included in the exported snapshot, not an editable input.

To convert an image displacement to millimeters, divide its pixel value by `m` (equivalently multiply by `1 / m`). Exporting this file does not modify image sequences or DIC inputs.

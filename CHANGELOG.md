# Changelog

## 1.1.0 — 2026-11-03

- New: GPNVG-18 housing mask, switchable and adjustable live in the F12 menu
  (section "GPNVG-18 Thermal - Mask"): on/off, size, horizontal/vertical
  offset, tube spacing, tube size, edge softness, housing opacity.
- The client plugin now draws the mask itself, sized to the screen's aspect
  ratio, so it always reaches the screen edges. Fixes the black bars that the
  BorkelRNVG night-vision mask produced in thermal mode (it is only a 30 %
  overlay outside the housing and was never meant for a full-screen thermal
  image). Works with and without BorkelRNVG.

## 1.0.0 — 2026-11-03

- First release (SPT 4.1.6).

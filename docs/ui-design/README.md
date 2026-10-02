# Lights Out UI design

Wyatt's target design for the game UI, 1280x720 per screen: `Main` (menu), `Prerace` (grid and tyre choice),
`Hud` (race HUD), `Lobby` (online room) and `Results`. Each file is a self-contained HTML mock; the markup and the
`renderVals()` sample data show exact sizes, colours and layout. The Unity UI follows these.

Design tokens:
- Font: Titillium Web (SIL OFL), weights 400/600/700/900 and 700 italic. Headings and big numbers are 700 italic
  uppercase with 0.04 to 0.08em tracking; small labels are 12px 600 uppercase, 0.1em tracking, in the dim colour.
  Numbers use tabular figures.
- Colours: ground #14151A, panel #1D1F26, idle control #2A2D37, rule #353945, dim text #A7ABB8, soft text #C9CCD6,
  text #FFFFFF, accent red #E0241B, yellow flag #FFD12E. Tyres: soft #E8352E, medium #FFD12E, hard #F2F0EB.
- Selected state: white fill with #14151A text (segmented controls, circuit list, your row in the tower/results).
- Shape: square corners except a rounded bottom-right corner (10px controls, 14 to 16px HUD panels, 24px screen
  panels, 28px dialogs). Primary panels have a 3px red top edge. Rows are split by 1px rules.
- Primary action: red fill, white 20px italic label on the left and an arrow on the right, 56px high.
- Tyre compounds are a dark disc with a coloured ring and the letter in the ring colour.
- No glows, gradients or neon in the UI; the cyberpunk look stays in the 3D world.

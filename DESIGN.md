---
name: PocketDeadlock
description: A focused native Windows workspace for Deadlock mod sets.
colors:
  moss-canvas: "#151A19"
  moss-panel: "#1E2623"
  moss-line: "#485A4E"
  warm-ink: "#F0EFE6"
  moss-muted: "#B8C3B9"
  lime-action: "#D5E998"
  deep-input: "#101613"
  moss-selection: "#303D2D"
  action-ink: "#172119"
  row-hover: "#809070"
  light-canvas: "#F0F3F0"
  light-panel: "#FAFCFA"
  light-line: "#6B806F"
  light-ink: "#19261D"
  light-muted: "#4D6251"
  light-action: "#CFE19A"
  light-input: "#E1E8DF"
  light-selection: "#E7EDDF"
typography:
  brand:
    fontFamily: Segoe UI
    fontSize: 26px
    fontWeight: 600
  headline:
    fontFamily: Segoe UI
    fontSize: 24px
    fontWeight: 600
  section:
    fontFamily: Segoe UI
    fontSize: 20px
    fontWeight: 600
  item-title:
    fontFamily: Segoe UI
    fontSize: 16px
    fontWeight: 600
  body:
    fontFamily: Segoe UI
    fontSize: 14px
    fontWeight: 400
  action:
    fontFamily: Segoe UI
    fontSize: 14px
    fontWeight: 600
  caption:
    fontFamily: Segoe UI
    fontSize: 12px
    fontWeight: 400
rounded:
  control: 4px
spacing:
  tight: 4px
  related: 8px
  compact: 10px
  row: 12px
  group: 16px
  pane: 20px
  window: 24px
components:
  button-primary:
    backgroundColor: "{colors.lime-action}"
    textColor: "{colors.action-ink}"
    typography: "{typography.action}"
    rounded: "{rounded.control}"
    padding: 10px 16px
  button-secondary:
    backgroundColor: "{colors.moss-panel}"
    textColor: "{colors.warm-ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: 10px 16px
  input:
    backgroundColor: "{colors.deep-input}"
    textColor: "{colors.warm-ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: 10px
  select:
    backgroundColor: "{colors.deep-input}"
    textColor: "{colors.warm-ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: 8px 10px
  catalog-row:
    backgroundColor: "{colors.moss-panel}"
    textColor: "{colors.warm-ink}"
    rounded: "{rounded.control}"
    padding: 12px
  catalog-row-selected:
    backgroundColor: "{colors.moss-selection}"
    textColor: "{colors.warm-ink}"
    rounded: "{rounded.control}"
    padding: 12px
  game-status:
    backgroundColor: "{colors.moss-panel}"
    textColor: "{colors.warm-ink}"
    rounded: "{rounded.control}"
    padding: 12px 16px
  detail-panel:
    backgroundColor: "{colors.moss-panel}"
    textColor: "{colors.warm-ink}"
    rounded: "{rounded.control}"
    padding: 20px
---

# Design System: PocketDeadlock

## Overview

**Creative North Star: "The Mod Workbench".** This descriptive name records the implemented utility, not a new visual direction. A moss-toned workspace lets players scan a library and work with a selected mod without losing context. A restrained lime accent identifies consequential actions and selection.

The native window and controls are part of the design. Use the same family, spacing, control vocabulary, and split workspace across catalogs, the library, profiles, downloads, and settings. Product tasks determine hierarchy; decoration does not compete with variants, requirements, priority, or game state.

**Key Characteristics:**

- Native Windows behavior with a single Segoe UI type family.
- Flat tonal surfaces and compact, squared-off controls.
- Persistent list context beside a selected-item detail pane.
- Two usable themes and plain Russian/English action labels.

The frontmatter is the token record. Runtime theme values come from `Localization.cs`; reusable templates come from `App.xaml`, and spatial roles from `MainWindow.xaml` and its partial classes. Runtime initialization supersedes the XAML fallback line color.

## Colors

The dark palette combines deep moss neutrals with warm readable ink; the light palette retains the same green character and role structure.

### Primary

- **Lime Action** (`lime-action`, `light-action`): primary actions and selected navigation. Also used for requirements, the caret, and activity progress.
- **Action Ink** (`action-ink`): text on either theme's filled primary button.

### Neutral

- **Moss Canvas / Light Canvas:** window background.
- **Moss Panel / Light Panel:** list rows, status band, detail pane, and secondary buttons.
- **Moss Line / Light Line:** thin component boundaries and footer separation.
- **Warm Ink / Light Ink:** primary text and keyboard-focus boundaries.
- **Moss Muted / Light Muted:** metadata, placeholders, and secondary status text.
- **Deep Input / Light Input:** editable fields and selection controls.
- **Moss Selection / Light Selection:** selected rows and text selection.
- **Row Hover:** the existing list-row hover boundary shared by both themes.

**The Action Accent Rule.** Reserve the accent for actions, selection, and meaningful status. Keep inactive rows and supporting text in neutral roles.

The `system` theme is resolved from Windows when the app starts or settings are saved. Continuous live synchronization with OS changes is not implemented.

## Typography

**UI Font:** Segoe UI. One familiar family carries branding, titles, controls, metadata, and prose. Semibold identifies headings and primary actions; regular carries body text.

### Hierarchy

- **Brand:** app name in the main header.
- **Headline:** selected-mod titles and settings heading.
- **Section:** profile-transfer and history headings.
- **Item Title:** catalog and library row names.
- **Body:** controls, descriptions, status, and general content.
- **Action:** filled primary actions.
- **Caption:** list metadata, game path, format help, and detail labels.

The detail description has a fixed line height (21px). Current local exceptions are the header subtitle (13px), saved-profile names (18px), and compact index hint (11px); they are not additional reusable heading roles. Long titles wrap. The game path trims with an ellipsis.

**The Task Type Rule.** Keep fixed desktop sizes. Do not add display fonts, fluid headings, or decorative monospace to ordinary UI labels.

## Layout

The main window starts at 1380 x 900 device-independent units and has a minimum size of 1100 x 760. WPF units map to CSS pixels at 96 DPI; Windows scales the window at higher DPI.

The outer inset is `window`. A header contains the product name and settings/game/launch actions. A full-width status band identifies the game and index state. The workspace has a flexible list area, a `pane` gap, and a fixed detail width (380 units). Detail content scrolls vertically with `pane` padding. There is no mobile breakpoint or automatic single-column collapse.

Navigation precedes search, filters, the scrollable result list, and pagination or apply controls. Navigation and action groups use wrapping panels. Feature pages reuse the list area and detail context. The footer holds progress, a wrapping status message, cancellation, and a staged-update action.

Use `related` gaps within actions, `row` padding and compact separation, and `group` or `pane` between groups. Do not expand every item into a large dashboard card.

## Elevation & Depth

There are no authored shadows, glass effects, or decorative gradients. Depth comes from canvas, panel and input tones, thin boundaries, and selection state. Native owner windows and dropdown popups supply platform layering.

**The Flat Workspace Rule.** Use tonal contrast and boundaries for resting surfaces. Do not add lift or glow to give a routine control visual weight.

## Shapes

Buttons, inputs, rows, the status band, and detail pane share the `control` radius. Default boundaries are one unit thick; button keyboard focus uses two units. Preserve native window chrome, checkboxes, scrolling, and dialog affordances.

## Components

### Buttons

Primary buttons use action fill; secondary buttons use panel fill and a thin line boundary. Both have a minimum height (40 units), centered content, and shared padding. Hover changes the boundary to accent; keyboard focus changes it to ink and thickens it. Disabled buttons use opacity (0.4). No authored pressed animation or transition exists.

### Inputs / Fields

Inputs use the input surface, ink text, muted placeholder, accent caret, and selection fill. Focus changes the boundary to ink. Single-line fields have a minimum height (40 units). The PD1 field is multiline and larger to hold a long key.

Combo boxes share the surface and minimum height. The chevron is a native Segoe MDL2 Assets glyph. Dropdowns use panel fill, line boundaries, selection fill, and a maximum popup height (360 units). Preserve arrow-key and popup behavior.

### Navigation

Navigation is a wrapping row of native buttons: Catalog, Library, Favorites, Downloads, and Profiles. The active page uses the primary style across all five destinations. Feature pages retain the surrounding window and show one clear current destination.

### Catalog / Library Rows

The catalog combines all providers internally and offers no provider switch. Below the main search, a 360-unit category button opens a native popup with a search field, matching categories, an explicit empty state, and an All categories reset. Down/Enter selects; Escape closes. Sorting stays in a small secondary menu at the right, rather than a persistent Recently updated selector. Matching listings share one row; content types and distinct origin identities remain separate.

A row places a semibold name above muted category/author or library metadata. Rows use `row` padding and `related` vertical gaps. Selection changes fill and boundary; hover changes the boundary; keyboard focus uses ink. Library rows add an enabled checkbox. Drag reordering and Higher/Lower change priority without changing row geometry.

### Status and Details

The status band holds running/applied state, index status, and game path. The detail pane presents title, metadata, preview, variant, actions, requirements, and description. Prioritize actionable status and recovery text. Progress appears in a thin bar (3 units), with status and cancellation in the footer.

### Dialogs

Use native file/folder selection and owner windows for settings and VPK choices. Preserve focus and predictable closing. Routine catalog and library navigation stays in the workspace.

## Do's and Don'ts

### Do:

- **Do** use role-matched light/dark resources and the runtime palette.
- **Do** retain visible focus and action labels in both languages.
- **Do** preserve the list/detail hierarchy and visible game state.
- **Do** wrap long names and messages, and verify both languages at the minimum size.
- **Do** distinguish downloading, selection, pending changes, and application through copy and controls.

### Don't:

- **Don't** add unsupported capabilities through design copy.
- **Don't** replace native behaviors with unfamiliar web affordances.
- **Don't** introduce shadows, glass, decorative motion, display fonts, or large pill shapes.
- **Don't** invent mobile layouts, animations, continuous OS-theme synchronization, or richer navigation states than the code implements.



# Unity UI code style

- Author hierarchy, templates, logical geometry, spacing, artwork, typography, 9-slice
  borders and interaction states in UXML and USS. Reuse the common theme and shared
  templates for repeated controls in the frontend, game and tutorial. Put semantic
  screen-specific modifiers in the screen's stylesheet. Keep hover, focus, pressed,
  selected and disabled states in USS.
- C# binds input, lifecycle and game data. Switch USS classes or use control states
  for data-driven variants. Set inline styles only for values that require runtime
  computation, such as a live HP fill, a camera-projected marker or viewport fit.
  Translated copy, live icons and rendered portraits are runtime data.
- Assign scene and prefab dependencies such as UIDocument, presenters and template
  assets with `[SerializeField]` when Unity can serialize the reference. Pass direct
  references to components created by code. Existing Addressables contracts can load
  assets selected dynamically. Avoid `GetComponent` inside frame updates and input
  callbacks. A legacy fallback may resolve a dependency once during initialization.
- Runtime objects cloned from UXML are VisualElements. Unity's scene/prefab serializer
  cannot bind those clones through `[SerializeField]`. Use typed custom UXML elements,
  direct references, and one-time queries by type or semantic class at view binding.
  UXML `name` may identify elements in UI Builder, diagnostics and tests. Runtime
  bindings in new or migrated views must avoid finding elements by `name`.
- Put shared 9-slice values in USS or use the sprite's authoritative import border
  when every consumer uses that border. Remember that Unity's Sprite border order
  is left, bottom, right, top. Use explicit USS sides when artwork has a screen-specific
  border. Check resolved styles and rendered corners at multiple viewport sizes.
- Attach windows to the appropriate shared `UiLayers` host. From back to front the
  hosts are HUD, windows, notifications, guide, dialogs, settings, cinematics,
  loading and application status. Keep sibling order inside a view in UXML where
  possible. Check pointer picking and keyboard focus when one window covers another.
- Compare font baselines inside their actual control rectangles in both locales.
  Adjust alignment and padding in the owning USS selector. Keep wrapping and
  single-line captions explicit so a change in font metrics cannot clip a control.

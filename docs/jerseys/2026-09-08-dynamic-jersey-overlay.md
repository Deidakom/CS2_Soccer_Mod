# CS2 dynamic jersey names and numbers

The kit package already supplies the four painted Source 2 player models, but
it does not contain the three Source 1 glyph models that the old CSS plugin used
for bonemerged letters and numbers. This revision adds the missing runtime
layer without requiring another Workshop upload:

- one public `point_worldtext` is created for each living player in kit mode;
- it follows the player's back and is refreshed at 32 Hz;
- the label is the sanitized player name (maximum ten ASCII characters) and a
  squad-unique number from 2–99;
- a goalkeeper always uses the fixed number `1`;
- the text is removed on death, disconnect, map change, unload, and whenever
  kit mode is disabled;
- there is no `CheckTransmit` hook, because that hook previously caused a
  server crash in this project.

Commands:

```text
css_sm2jerseydynamic          # status (match permission for changes)
css_sm2jerseydynamic on|off   # toggle the public overlay
css_sm2jerseynumber            # show/assign a session number
css_sm2jerseynumber 2-99      # choose an unused number for your squad
css_sm2jerseynumber random     # choose a new number
```

This is the functional CS2 equivalent of the CSS feature, but it is a world
text overlay rather than a texture baked into the jersey mesh. A later asset
pass can replace the overlay with Source 2 glyph models while retaining the
same allocation and name-normalization code.

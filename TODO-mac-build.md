# TODO: macOS Build for v2.2.4-SU

Build and upload the macOS asset to the existing GitHub release.

## Prerequisites

- macOS machine with Node.js installed
- `node_modules-darwin/` present in repo root
- GitHub CLI (`gh`) authenticated

## Steps

1. **Build the F# sources** (if not already done on this machine):
   ```bash
   node scripts/build.js
   ```

2. **Package the macOS app:**
   ```bash
   node scripts/package.js darwin
   ```
   This creates `dist-darwin/VisUAL2-SU-darwin-x64/` and a DMG.

3. **Zip the output:**
   ```bash
   cd dist-darwin
   zip -r VisUAL2-SU-v2.2.4-macOS-x64.zip VisUAL2-SU-darwin-x64/
   ```

4. **Upload to the existing release:**
   ```bash
   gh release upload v2.2.4-SU dist-darwin/VisUAL2-SU-v2.2.4-macOS-x64.zip -R rensutheart/Visual2
   ```

5. **Delete this file** once the upload is confirmed.

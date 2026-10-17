# Putting Placemaker on Paradox Mods

**What it means.** Paradox Mods is the game's official mod store: the same list players see in the game's Paradox
Mods menu. Uploading sends a copy of the mod, its page text (`MOD-PAGE.md`) and its pictures to Paradox under your
Paradox account. Anyone can then subscribe, and the game downloads it for them. Later fixes go up as new versions,
which players get automatically.

**How.** Code mods have no upload button on the website. The game's modding kit includes an uploader
(`ModPublisher.exe`) that runs on this computer and uses the Paradox login you last used in the game. It reads
`Properties/PublishConfiguration.xml`.

## Before the first upload

1. **Credits wording.** Decide between "inspired by" and "uses code from" (the code really contains ported
   ParkingLotTool and ParkManager code).
2. **Your co-author's OK.** Grid Road Generator code is in Placemaker, and that mod has no licence yet. The code
   comments disagree too (`Core/Tool/RoadGraph.cs` says MIT, `BuildService.Terrain.cs` says GPL-3.0); settle it.
3. **Public source code.** The GPL-3.0 licence requires it: a public GitHub repository, linked on the page.
4. **Pictures.** A thumbnail (`Properties/Thumbnail.png`; other mods use 512×512 to 1254×1254) and 5–8 screenshots
   (1920×1080). The game runner can take them.
5. **Check the page's claims in the game**: undo after a build, and what happens to built places when the mod is
   removed (put it on the page if they vanish).
6. **Sign in to your Paradox account in the game** once, if you haven't lately.

## The first upload (a private test)

7. Build a clean release into a separate folder with only the mod's files (no logs, no debug files). The project's
   normal build doesn't run the uploader, so this needs a small upload script.
8. Upload with `AccessLevel` = Private. The uploader prints the mod's number: write it into `ModId`.
9. Remove the local copy (`Mods\Placemaker`), subscribe to the private mod in your game, and check it works.
10. Switch `AccessLevel` to Public and run the uploader's update. Placemaker is now public.

## Every later update

- Raise `ModVersion` (0.1.1, 0.2.0…) and write the `ChangeLog`; the uploader refuses the same version twice.
- Upload as a new version. Subscribers get it the next time they start the game.

## Rules worth knowing

- Mods must be free (donation links are allowed) and contain only things you may share (Paradox user agreement, §5).
- No review before it goes live, but reported mods are checked, and Paradox can remove them.
- A name stays reserved even after the mod is deleted.

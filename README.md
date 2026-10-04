<div align="center"><h1>ShadowONE</h1>
<img src="https://raw.githubusercontent.com/ShadowTheHedgehogHacking/ShadowONE/refs/heads/main/res/preview.png" align="center" />
</div>

### About 

ShadowONE is a successor to the HeroesONE and HeroesONE-Reloaded ONE file editors.

* Full linux support including auto .desktop integration (asks if you want to add it on first launch)
* Auto reg/file association for Windows
* Search/filter content in a .ONE for fast searching
* Internal file move action -> Shift+S (Shift Down) and Shift+W (Shift Up) or Drag within the .one to deal with pesky parser restrictions
* Show compression/decompression metadata
* Rename, Replace, Extract, Delete, Add Files, Extract All
* 'Sort by Extensions' feature to align a .one with in-game parser restrictions
* Edit RW Version of a single file, all files, or the archive itself


### Drag & Drop / Double Click

You can drag & drop files to the editor.


Dragging from the editor to the same editor will allow you to move entries
* Drop it on top of an entry to put it at that entry's position. Dropping on empty space moves it to the end.


Dragging to the editor from your file system...
* If a single .one is dropped, it will be opened, discarding the currently loaded data.
* If a file dropped matches the name of an item already in the loaded data, it will replace/update the loaded data.
* If a file dropped does not match the name of an item already in the loaded data, it is inserted at the position after the currently selected item. If no item is selected it is appended to the end of the items.


Dragging from the editor to your file system...
* Will allow you to copy single files directly (equivalent to extract feature)
* Will allow you to drag directly into other programs (DFF model viewer, Texture tools etc)
* One file at a time, you have to move the mouse a bit before the drag kicks in


Double clicking an entry attempts to open said file

### Thanks

* [HeroesONE - The OG .ONE UI program](https://github.com/sonicretro/HeroesONE)
* [HeroesONE-Reloaded - The successor to HeroesONE](https://github.com/Sewer56/HeroesONE-Reloaded)
* [HeroesONE-R - C# library using the new Rust prs_rs](https://github.com/Heroes-Hacking-Central/HeroesONE-R)
* [prs-rs - new Rust PRS library](https://github.com/Sewer56/prs-rs)

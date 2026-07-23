<div align="center"><h1>ShadowONE</h1>
<img src="https://raw.githubusercontent.com/ShadowTheHedgehogHacking/ShadowONE/refs/heads/main/res/preview.png" align="center" />
</div>

### About 

ShadowONE is a successor to the HeroesONE and HeroesONE-Reloaded ONE file editors.

* Full linux support including auto .desktop integration on run
* Auto reg/file association for Windows
* Search/filter content in a .ONE for fast searching
* Internal file move action -> Shift+S (Shift Down) and Shift+W (Shift Up) or Drag within the .one to deal with pesky parser restrictions
* Show compression/decompression metadata


### Drag & Drop / Double Click

You can drag & drop files to the editor.


Dragging from the editor to the same editor will allow you to move entries


Dragging to the editor from your file system...
* If a single .one is dropped, it will be opened, discarding the currently loaded data.
* If a file dropped matches the name of an item already in the loaded data, it will replace/update the loaded data.
* If a file dropped does not match the name of an item already in the loaded data, it is inserted at the position after the currently selected item. If no item is selected it is appended to the end of the items.


Dragging from the editor to your file system...
* Will allow you to copy single files directly (equivalent to extract feature)
* Will allow you to drag directly into other programs (DFF model viewer, Texture tools etc)
# dmgbuild settings for the Fluent for Mac disk image (the drag-into-Applications window).
# Used by scripts/build-dmg.sh:  dmgbuild -s dmg/settings.py -D app=<Fluent.app> -D dmgdir=<this folder> "Fluent" <out.dmg>
import os

app = defines["app"]  # noqa: F821 (provided by dmgbuild)
here = defines["dmgdir"]  # noqa: F821

format = "UDZO"
filesystem = "HFS+"
files = [app]
symlinks = {"Applications": "/Applications"}
icon = os.path.join(os.path.dirname(here), "Resources", "AppIcon.icns")   # the volume's icon
badge_icon = None

background = os.path.join(here, "background.tiff")
window_rect = ((200, 160), (660, 472))  # 440 of content plus the title bar
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 112
text_size = 13
icon_locations = {"Fluent.app": (165, 200), "Applications": (495, 200)}

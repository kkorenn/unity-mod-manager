# dmgbuild settings — styled DMG layout, written headlessly (no Finder automation).
# Invoked by build-app.sh:
#   dmgbuild -s dmg-settings.py -D app=<app> -D background=<png> -D icon=<icns> "Vol" out.dmg
import os.path

app = defines.get("app")
appname = os.path.basename(app)
bg = defines.get("background")
vicon = defines.get("icon")

format = defines.get("format", "UDZO")
files = [app]
symlinks = {"Applications": "/Applications"}
if vicon:
    icon = vicon
if bg:
    background = bg

default_view = "icon-view"
include_icon_view_settings = True
include_list_view_settings = False
show_status_bar = False
show_toolbar = False
show_pathbar = False
show_sidebar = False

window_rect = ((200, 120), (600, 400))
icon_size = 100
text_size = 13
icon_locations = {
    appname: (150, 220),
    "Applications": (450, 220),
}

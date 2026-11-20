#!/usr/bin/env python3
"""Snímek obrazovky přes xdg-desktop-portal (GNOME Wayland). Použití: shot.py <cil.png>"""
import shutil, sys, urllib.parse
import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

target = sys.argv[1]
bus = Gio.bus_get_sync(Gio.BusType.SESSION)
loop = GLib.MainLoop()
result = {}

def on_response(conn, sender, path, iface, signal, params):
    code, res = params.unpack()
    result["code"], result["uri"] = code, res.get("uri")
    loop.quit()

token = "cc%d" % GLib.random_int()
sender = bus.get_unique_name()[1:].replace(".", "_")
handle = f"/org/freedesktop/portal/desktop/request/{sender}/{token}"
bus.signal_subscribe("org.freedesktop.portal.Desktop", "org.freedesktop.portal.Request", "Response",
                     handle, None, Gio.DBusSignalFlags.NONE, on_response)
bus.call_sync("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
              "org.freedesktop.portal.Screenshot", "Screenshot",
              GLib.Variant("(sa{sv})", ("", {"handle_token": GLib.Variant("s", token),
                                             "interactive": GLib.Variant("b", False)})),
              None, Gio.DBusCallFlags.NONE, -1, None)
GLib.timeout_add_seconds(60, loop.quit)
loop.run()
if result.get("code") != 0 or not result.get("uri"):
    sys.exit(f"snímek se nepovedl: {result}")
src = urllib.parse.unquote(urllib.parse.urlparse(result["uri"]).path)
shutil.move(src, target)
print(target)

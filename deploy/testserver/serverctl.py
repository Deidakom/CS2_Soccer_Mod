#!/usr/bin/env python3
"""Standard-library helpers for update-server.sh (Python 3.8+).

  a2s HOST PORT                   one line: players=N bots=N max=N map=NAME
  rcon HOST PORT ENVFILE CMD...   run console commands over Source RCON. The
                                  password is read from CS2_RCON_PASSWORD in
                                  ENVFILE so it never appears in a process list.
  buildid-latest FILE             public-branch build id in steamcmd
                                  +app_info_print output
  buildid-installed FILE          build id in steamapps/appmanifest_730.acf
  gameinfo-metamod FILE           make sure Metamod's search path is present;
                                  prints "present" or "added"
  unzip ZIP DEST                  extract, keeping Unix permissions and links
  github-asset JSONFILE WORD...   "<tag> <url>" of the first release asset whose
                                  name contains every WORD
  workshop-status ID...           one line per Workshop item: whether Steam lists
                                  it publicly (exit 1 if not). A pending approval
                                  only shows when downloading: set-addons.sh
  mam-addons CFG [ID...|none]     print or set mm_extra_addons in a
                                  MultiAddonManager cfg, leaving other lines alone
"""

import json
import os
import re
import shutil
import socket
import stat
import struct
import sys
import tempfile
import urllib.parse
import urllib.request
import zipfile

A2S_INFO_REQUEST = b"\xff\xff\xff\xffTSource Engine Query\x00"
METAMOD_SEARCH_PATH = "csgo/addons/metamod"


def _cstring(data, offset):
    end = data.index(b"\x00", offset)
    return data[offset:end].decode("utf-8", "replace"), end + 1


def a2s_info(host, port, timeout=3.0):
    """Query A2S_INFO, answering the challenge current servers require."""
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.settimeout(timeout)
        sock.sendto(A2S_INFO_REQUEST, (host, port))
        data, _ = sock.recvfrom(4096)
        for _ in range(3):
            if len(data) >= 9 and data[:5] == b"\xff\xff\xff\xffA":
                sock.sendto(A2S_INFO_REQUEST + data[5:9], (host, port))
                data, _ = sock.recvfrom(4096)
                continue
            break
    if len(data) < 6 or data[:5] != b"\xff\xff\xff\xffI":
        raise ValueError("not an A2S_INFO reply")
    offset = 6  # header, 'I', protocol
    name, offset = _cstring(data, offset)
    map_name, offset = _cstring(data, offset)
    _folder, offset = _cstring(data, offset)
    _game, offset = _cstring(data, offset)
    offset += 2  # app id
    players, max_players, bots = data[offset], data[offset + 1], data[offset + 2]
    return {"name": name, "map": map_name, "players": players, "max": max_players, "bots": bots}


def read_env_value(path, key):
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            name, value = line.split("=", 1)
            if name.strip() != key:
                continue
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
                value = value[1:-1]
            return value
    raise KeyError(key)


def rcon(host, port, password, commands, timeout=5.0):
    """Run commands over Source RCON and return one output string per command."""
    with socket.create_connection((host, port), timeout=timeout) as sock:
        def send(request_id, kind, body):
            payload = struct.pack("<ii", request_id, kind) + body.encode("utf-8") + b"\x00\x00"
            sock.sendall(struct.pack("<i", len(payload)) + payload)

        def read_exact(count):
            buffer = b""
            while len(buffer) < count:
                chunk = sock.recv(count - len(buffer))
                if not chunk:
                    raise ConnectionError("RCON connection closed")
                buffer += chunk
            return buffer

        def receive():
            (size,) = struct.unpack("<i", read_exact(4))
            if size < 10 or size > 4 * 1024 * 1024:
                raise ValueError("malformed RCON packet")
            data = read_exact(size)
            request_id, kind = struct.unpack("<ii", data[:8])
            return request_id, kind, data[8:-2].decode("utf-8", "replace")

        send(1, 3, password)  # SERVERDATA_AUTH
        while True:
            request_id, kind, _ = receive()
            if kind == 2:  # SERVERDATA_AUTH_RESPONSE
                if request_id == -1:
                    raise PermissionError("RCON authentication failed")
                break

        outputs = []
        for index, command in enumerate(commands):
            command_id = 100 + index * 2
            send(command_id, 2, command)  # SERVERDATA_EXECCOMMAND
            # An empty response packet is echoed after the command's output,
            # marking the end of multi-packet replies.
            send(command_id + 1, 0, "")
            parts = []
            while True:
                try:
                    request_id, _, body = receive()
                except socket.timeout:
                    break  # servers that never echo the marker
                if request_id == command_id + 1:
                    break
                if request_id == command_id:
                    parts.append(body)
            outputs.append("".join(parts))
        return outputs


def latest_buildid(text):
    branches = re.search(r'"branches"\s*\{\s*"public"\s*\{(.*?)\}', text, re.S)
    if not branches:
        return None
    build = re.search(r'"buildid"\s*"(\d+)"', branches.group(1))
    return build.group(1) if build else None


def installed_buildid(text):
    build = re.search(r'"buildid"\s*"(\d+)"', text)
    return build.group(1) if build else None


def ensure_metamod_search_path(path):
    """CS2 updates restore gameinfo.gi and drop Metamod's search path, which
    silently disables Metamod, CounterStrikeSharp and every plugin."""
    with open(path, encoding="utf-8", newline="") as handle:
        lines = handle.read().splitlines(keepends=True)
    present = re.compile(r"^\s*Game\s+" + re.escape(METAMOD_SEARCH_PATH) + r"\s*(//.*)?$")
    if any(present.match(line.rstrip("\r\n")) for line in lines):
        return "present"

    def entry(template_line):
        indent = re.match(r"[ \t]*", template_line).group(0)
        ending = "\r\n" if template_line.endswith("\r\n") else "\n"
        return f"{indent}Game\t{METAMOD_SEARCH_PATH}{ending}"

    # Metamod's documented position: first entry after Game_LowViolence.
    for index, line in enumerate(lines):
        if re.match(r"^\s*Game_LowViolence\b", line):
            lines.insert(index + 1, entry(line))
            break
    else:
        for index, line in enumerate(lines):
            if re.match(r"^\s*Game\s+csgo\s*(//.*)?$", line.rstrip("\r\n")):
                lines.insert(index, entry(line))
                break
        else:
            raise ValueError("gameinfo.gi SearchPaths layout not recognised")

    replace_file(path, lines)
    return "added"


def replace_file(path, lines):
    """Atomically rewrite a file, keeping its mode and owner."""
    directory = os.path.dirname(os.path.abspath(path))
    status = os.stat(path)
    handle, temporary = tempfile.mkstemp(dir=directory, prefix="." + os.path.basename(path) + ".")
    try:
        with os.fdopen(handle, "w", encoding="utf-8", newline="") as output:
            output.writelines(lines)
        os.chmod(temporary, stat.S_IMODE(status.st_mode))
        try:
            os.chown(temporary, status.st_uid, status.st_gid)
        except PermissionError:
            pass
        os.replace(temporary, path)
    except BaseException:
        if os.path.exists(temporary):
            os.unlink(temporary)
        raise


# The exact convar: "mm_extra_addons_timeout" must never match.
MAM_ADDONS_LINE = re.compile(r'^\s*mm_extra_addons\s+"?([0-9,\s]*)"?\s*(//.*)?$')


def read_mam_addons(path):
    with open(path, encoding="utf-8", newline="") as handle:
        for line in handle.read().splitlines():
            match = MAM_ADDONS_LINE.match(line)
            if match:
                return [item for item in re.split(r"[,\s]+", match.group(1)) if item]
    return []


def write_mam_addons(path, ids):
    """Set mm_extra_addons to exactly these ids: one line, other settings kept."""
    if any(not item.isdigit() for item in ids):
        raise ValueError("Workshop ids are numbers")
    with open(path, encoding="utf-8", newline="") as handle:
        lines = handle.read().splitlines(keepends=True)
    entry = 'mm_extra_addons "' + ",".join(ids) + '"'
    output, written = [], False
    for line in lines:
        body = line.rstrip("\r\n")
        if MAM_ADDONS_LINE.match(body) or re.match(r"^\s*mm_extra_addons\s*$", body):
            if not written:
                output.append(entry + (line[len(body):] or "\n"))
                written = True
            continue  # drop duplicates
        output.append(line)
    if not written:
        output.insert(0, entry + "\n")
    replace_file(path, output)
    return ids


def safe_unzip(archive_path, destination):
    root = os.path.realpath(destination)
    os.makedirs(root, exist_ok=True)
    with zipfile.ZipFile(archive_path) as archive:
        for info in archive.infolist():
            target = os.path.realpath(os.path.join(root, info.filename))
            if target != root and not target.startswith(root + os.sep):
                raise ValueError(f"unsafe path in archive: {info.filename}")
            mode = info.external_attr >> 16
            if info.is_dir():
                os.makedirs(target, exist_ok=True)
                continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            if stat.S_ISLNK(mode):
                link = archive.read(info).decode("utf-8")
                if os.path.isabs(link) or not os.path.realpath(
                        os.path.join(os.path.dirname(target), link)).startswith(root + os.sep):
                    raise ValueError(f"unsafe link in archive: {info.filename}")
                if os.path.lexists(target):
                    os.unlink(target)
                os.symlink(link, target)
                continue
            with archive.open(info) as source, open(target, "wb") as output:
                shutil.copyfileobj(source, output)
            if stat.S_IMODE(mode):
                os.chmod(target, stat.S_IMODE(mode))


def github_asset(release, words):
    for asset in release.get("assets", []):
        name = asset.get("name", "")
        if all(word in name for word in words):
            return release.get("tag_name", ""), asset.get("browser_download_url", "")
    raise LookupError("no release asset matches " + " ".join(words))


STEAM_FILE_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"


def workshop_status(details):
    """Classify one GetPublishedFileDetails entry. Private, friends-only and
    unfinished items all answer result 9 to anyone but their owner, and
    MultiAddonManager then cannot deliver them to joining players."""
    item = details.get("publishedfileid", "?")
    if details.get("result") != 1:
        return item, False, "not downloadable by others (private, friends-only, unapproved or missing)"
    problems = []
    if details.get("visibility") != 0:
        problems.append(f"visibility {details.get('visibility')} (not public)")
    if details.get("banned"):
        problems.append("banned")
    if not int(details.get("file_size") or 0):
        problems.append("no content uploaded")
    if problems:
        return item, False, ", ".join(problems)
    return item, True, f"listed publicly, {int(details['file_size']):,} bytes, \"{details.get('title', '')}\""


def fetch_workshop_details(ids, timeout=20.0):
    fields = {"itemcount": str(len(ids))}
    fields.update({f"publishedfileids[{i}]": item for i, item in enumerate(ids)})
    request = urllib.request.Request(STEAM_FILE_DETAILS, data=urllib.parse.urlencode(fields).encode())
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.load(response)["response"]["publishedfiledetails"]


def main(argv):
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    command, args = argv[1], argv[2:]
    try:
        if command == "a2s" and len(args) == 2:
            info = a2s_info(args[0], int(args[1]))
            print(f"players={info['players']} bots={info['bots']} max={info['max']} map={info['map']}")
        elif command == "rcon" and len(args) >= 4:
            password = read_env_value(args[2], "CS2_RCON_PASSWORD")
            for output in rcon(args[0], int(args[1]), password, args[3:]):
                print(output.rstrip("\n"))
        elif command == "buildid-latest" and len(args) == 1:
            with open(args[0], encoding="utf-8", errors="replace") as handle:
                build = latest_buildid(handle.read())
            if not build:
                return 1
            print(build)
        elif command == "buildid-installed" and len(args) == 1:
            with open(args[0], encoding="utf-8", errors="replace") as handle:
                build = installed_buildid(handle.read())
            if not build:
                return 1
            print(build)
        elif command == "gameinfo-metamod" and len(args) == 1:
            print(ensure_metamod_search_path(args[0]))
        elif command == "unzip" and len(args) == 2:
            safe_unzip(args[0], args[1])
        elif command == "github-asset" and len(args) >= 2:
            with open(args[0], encoding="utf-8") as handle:
                tag, url = github_asset(json.load(handle), args[1:])
            print(f"{tag} {url}")
        elif command == "mam-addons" and len(args) == 1:
            print(",".join(read_mam_addons(args[0])) or "none")
        elif command == "mam-addons" and len(args) >= 2:
            ids = [] if args[1:] == ["none"] else list(dict.fromkeys(args[1:]))
            print(",".join(write_mam_addons(args[0], ids)) or "none")
        elif command == "workshop-status" and args and all(item.isdigit() for item in args):
            healthy = True
            for details in fetch_workshop_details(args):
                item, ok, text = workshop_status(details)
                healthy &= ok
                print(f"{item} {'ok' if ok else 'BROKEN'}: {text}")
            if not healthy:
                return 1
        else:
            print(__doc__, file=sys.stderr)
            return 2
    except (OSError, ValueError, KeyError, LookupError, PermissionError) as error:
        print(f"serverctl {command}: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

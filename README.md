# DownloadServerCLI

[![CI](https://github.com/OpenChargingCloud/DownloadServerCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/DownloadServerCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/DownloadServerCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/DownloadServerCLI/actions/workflows/nightly.yml)

One download server for charging stations and local controllers: it offers
files - firmware images first of all, and whatever else they are pointed to -
by HTTP GET on a port of its own and over SFTP on its SSH server, takes a
download that stopped up where it stopped, records who fetched what, and shows
both in a web interface, a JSON API and a prompt - at the console and over
SSH - until 'quit' or Ctrl+C.

What a charging station is given as the location of OCPP's UpdateFirmware or
SignedUpdateFirmware is a download URL of this server, with the credentials of
its account in it; whatever it fetches is recorded at once - by time, filtered
by downloader and by file.

It is a sibling of [LogServerCLI](https://github.com/OpenChargingCloud/LogServerCLI),
[DNSServerCLI](https://github.com/OpenChargingCloud/DNSServerCLI),
[NTSServerCLI](https://github.com/OpenChargingCloud/NTSServerCLI),
[PKICLI](https://github.com/OpenChargingCloud/PKICLI) and the others, and is
built the same way. The HTTP and SSH servers below it are
[Hermod](https://github.com/Vanaheimr/Hermod)'s; what every program of the
family has - the sign-in, the name servers it asks itself, the groups of time
servers and the certificates they are held to, the SSH server with its command
line and its SFTP tree, and the log - is
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node); what is the
download server's own is in [DownloadServer](https://github.com/OpenChargingCloud/DownloadServer).


### Getting it

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules https://github.com/OpenChargingCloud/DownloadServerCLI.git
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

**On Windows**, turn long paths on first: `git config --global core.longpaths true`.


### Building and running it

.NET 11 and Node 22 or newer. The build runs `npm ci` and `npm run build` for
the web interface itself whenever its inputs changed, and embeds it into the
assembly; `-p:SkipFrontendBuild=true` builds the rest alone.

```
dotnet build DownloadServerCLI.slnx
dotnet run --project DownloadServerCLI
```

or `./run.sh`, which takes the same switches. `--help` lists all of them.
Without a configuration file it offers the files of one area, `firmware`, by
HTTP GET on port 8365, on every address of both families, and over SFTP on
its SSH server, from `files/firmware/` beside the configuration file - and
serves its web interface, its JSON API and its command line over SSH on
127.0.0.1 only: http://127.0.0.1:2365/ and port 22365 (`--any` for every
address, which SFTP then listens on as well). At the first start it makes one
account, `root`, and shows its password once, on the console.

Every switch of the download server says for one start what the
`downloadServer` section says for every start:

| switch | |
|---|---|
| `--download-port <n>` | the TCP port files are offered on by HTTP GET (8365) |
| `--download-listen <address>` | where files are offered by HTTP GET (`any`, every address of both families) |
| `--download-https` | HTTP downloads over TLS, with the certificate store's identity for the listener `download` |
| `--download-https-cert <file>` | the same with a certificate in a PEM file - certbot's `fullchain.pem` |
| `--download-https-key <file>` | its key, where that is a file of its own - certbot's `privkey.pem` |
| `--download-url <url>` | the URL charging stations reach the download port at (guessed where not given) |
| `--files <directory>` | where the areas and the record of the downloads lie (`files` beside the configuration file) |
| `--areas <area>[,<area>...]` | the areas files are offered in (`firmware`) |
| `--no-http-downloads` | nothing offered by HTTP GET |
| `--no-sftp-downloads` | nothing offered over SFTP |
| `--anonymous-downloads` | HTTP downloads without credentials as well |

A start that cannot have its port ends there and says which switch to give
instead.


### Areas and files

The files lie in areas: one directory each below `directory`, and the first
part of the path a file is fetched by - `/firmware/station-4.2.1.bin`. The
disk is what is offered: a file is offered as soon as it is in its area's
directory, and no longer once it is gone. Files are published

* on the **Files** page, or with `PUT /api/v1/downloadServer/files/<area>/<name>` -
  in one request up to what the web interface's HTTP server takes as one,
  a larger file in parts (`?offset=<byte>`, the last with `&final=true`), as
  the Files page sends it. That server waits five minutes for the body of
  one request - `"web": { "requestBodyTimeout": <seconds> }` in the
  configuration file says how long otherwise, WWCP_Node's key, a second to a
  day -: over a line too slow for a part of 4 MiB in that time, the
  Files page sends it again half as large - down to 64 KiB - and nothing of
  the part that took too long is kept;
* over SFTP, by an account that may publish them - the `publisher` role, or
  an administrator: `sftp> put station-4.2.1.bin /firmware/`;
* by putting them in the area's directory by hand.

A file published over the JSON API or over SFTP is written under a hidden name
and moved into place once it is complete, so that nobody downloads half of it;
what a publication that never finished left behind is taken away at the next
start. `files remove firmware/station-4.2.1.bin` - or the button beside it -
offers a file no more; what was downloaded of it stays recorded.


### Downloaders

Files are downloaded under accounts of the node in the `downloader` role - one
a charging station or a local controller, as a rule, named as it is. They are
made on the **Downloaders** page, or at the prompt:

```
downloaders add CS001 The charger in the yard
```

which answers with the password of the new account, shown this once.
`downloaders password CS001` makes it a new one, `downloaders remove CS001`
takes the role away again; what it downloaded stays recorded.


### Downloading by HTTP GET

```
GET http://<host>:8365/<area>/<file name>
```

Signed in with HTTP Basic Auth as a downloader - or without credentials, where
`"anonymousHTTP": true` (or `--anonymous-downloads`) says so. What a charging
station is given as the location of UpdateFirmware:

```
http://CS001:<password>@firmware.example.org:8365/firmware/station-4.2.1.bin
```

and to try it by hand:

```
curl -u CS001:<password> -O http://127.0.0.1:8365/firmware/station-4.2.1.bin
```

It answers `200` with the file, or `206` with one `Range` of bytes of it - a
download that stopped takes up where it stopped; `If-Range` by the file's
SHA-256 (its `ETag`) or by its time of writing; `HEAD` for what a `GET` would
say. `401` without credentials (or with wrong ones), `403` for an account that
may not download, `404` for a file that is not offered, `416` for a range
beyond the end. Nothing else is served on that port.

The file - or the range of it - is sent from the disk as the charging station
reads it, and never held in memory: a download costs a buffer or two of
80 KiB, however large the file. There is no time set for the whole of it, so a
charging station on a slow line takes as long as it needs; one that stops
reading for ten minutes counts as having given up, and is recorded with what
it read.

The download port is an HTTP server of its own, and has a certificate of its
own: `"downloadServer": { "tls": ... }` - or `--download-https` - and not the
web interface's `web.tls`, so that either may speak TLS without the other, and
each under the name it is reached by. As `web.tls`, it is
`{ "certificate": "store" }` - the certificate store's TLS server identity for
the listener `download`, or one told no listener; an identity told `web` only
is never shown here - or `{ "certificate": { "fullchain": "<file>",
"privateKey": "<file>" } }`, read again whenever the files change. With
nothing to show, a connection is refused rather than served in the clear.

Over plain HTTP the file can be changed on its way and the password crosses
the network in the clear, and the download server says so at every start -
unless `publicURL` says `https`, which means a proxy in front speaks TLS.

The URL the banner, the prompt and the web interface tell charging stations
is `publicURL` - `"https://firmware.example.org:8365/"`, or a proxy's, a path
below it included - or `--download-url`; where neither says it, it is
guessed: the address the port listens on, or the name of this machine where
it listens on every address, and the log says so.


### Downloading over SFTP

The node's SSH server serves an SFTP tree, and the download server adds one
directory an area to it. A downloader signs in with one of its SSH keys -
given to it with `sshKeys` or `--authorize-ssh-key CS001=<file>` - or, where
the `ssh` section says `"passwords": true`, with its password, and fetches:

```
sftp -P 22365 CS001@firmware.example.org
sftp> get /firmware/station-4.2.1.bin
```

A downloader may read the areas, and write nothing in them.


### The configuration

`configuration.json` below the repository root, or what `--config` names. The
section of the download server, with every key and its default:

```json
{
  "downloadServer": {
    "directory":      "files",
    "areas":          [ "firmware" ],
    "http":           true,
    "httpListen":     "any",
    "httpPort":       8365,
    "tls":            null,
    "publicURL":      null,
    "anonymousHTTP":  false,
    "sftp":           true,
    "maxFileSize":    1073741824
  },
  "ssh": {
    "passwords":      true
  }
}
```

`tls` is `{ "certificate": "store" }` or `{ "certificate": { "fullchain":
"<file>", "privateKey": "<file>" } }` for HTTPS on the download port, paths
beside the configuration file, and no `redirectPort`; `publicURL` the URL
charging stations are told, `http` or `https`, without credentials, query or
fragment.

An area is named with lower-case letters, digits, `-` and `_`; `logs` is
every node's own over SFTP and cannot be one. SFTP is served by default on a
download server; `"ssh": { "sftp": false }` switches it off for the whole node.


### What is recorded

Every download, by HTTP or over SFTP, in `downloads.jsonl` beside the areas,
one line each: its number and time, the protocol, the downloader, the area and
the file, the status it was answered with, from which byte on and how many
bytes, how large the file was then and whether it was fetched to its end, the
address it came from, and for HTTP the User-Agent it said.

A download over HTTP is recorded once its last byte has been read - or, where
the client gave up, once nothing more was read of it for ten minutes - with
the bytes read and sent for it. What arrived the server cannot know: a file
that lay whole in the TCP buffers when the client went away is recorded as
fetched whole.


### The prompt

Beside every node's commands:

* `downloadServer` - where it offers files, how many in which area, the
  newest download;
* `files [<area> | remove <area>/<file name>]` - the files offered, of every
  area or of one; or one offered no more;
* `downloads [<downloader>]` - the newest downloads, of every downloader or
  of one;
* `downloaders [add <name> [<what it is called>] | password <name> | remove <name>]`.

The prompt is the console's, and every account's over SSH:
`ssh -p 22365 root@127.0.0.1`.


### The web interface

http://127.0.0.1:2365/, signed in with an account of the node. Beside what
every node shows - its configuration, the name and time servers it asks
itself, the SSH server, the certificate store and the log - the download
server has four pages of its own:

* **Files** - every file offered, area by area: its size, when it was
  written, its SHA-256 and how often it was downloaded; each to save and to
  take away, and a file to publish. A file published, taken away or
  downloaded elsewhere appears as it happens.
* **Downloads** - every download: when, by which downloader, which file, how
  much of it from which byte on, HTTP or SFTP, from where; newest or oldest
  first, filtered by downloader and by file (kept in the address of the page,
  so that it can be sent as a link).
* **Download server** - how files are offered, the URL a charging station is
  given and where the port listens, the certificate the download port shows,
  the `curl` and `sftp` lines to try it with, and what is offered.
* **Downloaders** - the accounts in the downloader role, how many downloads
  each made and when last; made with a form, given a new password, taken away.

The frontend is TypeScript and SCSS, drawn with lit-html and bundled by
webpack, in `libs/DownloadServer/DownloadServer/Frontend`; what every kind of
node shares comes from `libs/WWCP_Node/Frontend`.


### The JSON API

Below http://127.0.0.1:2365/api/, beside every node's routes, for an account
signed in at `/ext/login`:

| | |
|---|---|
| `GET  v1/downloadServer` | how the download server stands |
| `GET  v1/downloadServer/settings` | the `downloadServer` section as it is in effect |
| `GET  v1/downloadServer/files` | the files offered - `?area=` for one area - and the areas there are |
| `GET  v1/downloadServer/files/{area}/{name}` | one file: its size, when it was written, its SHA-256 |
| `GET  v1/downloadServer/files/{area}/{name}/content` | the file itself, to save |
| `PUT  v1/downloadServer/files/{area}/{name}` | the body offered as that file, in the place of the one of that name |
| `PUT  v1/downloadServer/files/{area}/{name}?offset=N` | one part of it, at byte N - `0` begins anew -, with `&final=true` the last; `409` with `received` where N is not where the parts before ended |
| `POST v1/downloadServer/files/{area}/{name}/remove` | the file offered no more |
| `GET  v1/downloadServer/downloads` | the downloads - `?downloader=`, `?area=`, `?file=`, `?since=`, `?until=`, `?order=newest\|oldest`, `?skip=`, `?take=` |
| `GET  v1/downloadServer/downloaders` | the downloaders |
| `POST v1/downloadServer/downloaders` | `{ "id", "name"? }` - a downloader more, answered with its password |
| `POST v1/downloadServer/downloaders/password` | `{ "id" }` - a new password |
| `POST v1/downloadServer/downloaders/remove` | `{ "id" }` - no longer a downloader |

The event stream `v1/events` says `download`, `filePublished` and
`fileRemoved` as they happen.


### Who may do what

Four resources beside the node's: `files` - read, listed and fetched through
the web interface; edited, published and taken away - `downloads` - read, the
record of every download - `downloaders` - read, listed; edited, made, given a
new password, taken away - and `download` - run, a file fetched by HTTP or
over SFTP. Three roles beside the node's viewer and administrators:

| role | |
|---|---|
| `downloader` | downloads files, and nothing else - a charging station, a local controller |
| `publisher` | sees, publishes and takes away files, and downloads them - a build pipeline; sees no downloads |
| `operator` | looks at everything, publishes and takes away files, asks the name and time servers; makes no downloaders |


### Where things are

| | |
|---|---|
| `DownloadServerCLI/` | the command line: the download server's own switches, and its lines of what the console says at a start |
| `libs/DownloadServer/` | the download server itself - its settings, the areas and their files, the download port, the SFTP areas, the record of the downloads, the downloaders, its JSON API, what can be typed at it - and its tests |
| `libs/DownloadServer/DownloadServer/Frontend/` | the web interface: TypeScript and SCSS, bundled by webpack |
| `libs/WWCP_Node/` | what every one of these programs shares |
| `libs/WWCP_Core/` | of WWCP Core only WWCP_CoreData, which WWCP_Node builds on |
| `libs/Hermod/` | the network below all of them: HTTP, TLS, SSH, SFTP and DNS |
| `libs/Norn/` | NTS and NTP, with which the node checks its clock |
| `libs/Styx/`, `libs/Urdr/` | the basics below that, and time stamps (RFC 3161) |
| `.github/workflows/` | what runs on every push, and what runs at night |
| `.github/scripts/` | the start both of them make: the server asked for its web interface, its JSON API, SSH, a file published and downloaded |


### Its tests

```
dotnet test libs/DownloadServer/DownloadServerTests/DownloadServerTests.csproj
```

A download server started on the loopback address: a downloader made over the
JSON API fetches a file by HTTP GET, whole and from a byte on; a download
without credentials, by an account that may not download, or of a file that
is not offered refused; a publisher publishes and takes away files but sees no
downloads; a new password and a removed downloader shut the old credentials
out; a file put in its area by hand offered at once, and fetched without
credentials where that is allowed; what a publication that never finished left
taken away at the next start; the download port on its own: 64 MiB fetched
whole and from a byte on for far less memory than they are large, a download
that stops reading for longer than half a minute served to its end, a
certificate of its own shown while the web interface speaks plain HTTP and,
with nothing in the store for it, a connection refused rather than served in
the clear, the URL charging stations are told, the new keys read - and
WWCP_Node's conformance suite asked of a download server, its web interface
among it. The web interface's own tests are
run by node, in `libs/DownloadServer/DownloadServer/Frontend` - among them a
file published in parts that are made smaller where one takes too long:

```
npm run typecheck
npm run typecheck:test
npm test
```

**CI** runs them on every push, beside the node's own tests, on Windows and on
Debian 13; on Debian it then starts the server and asks it: `/` must be the
embedded web interface, the JSON API must be shut to anybody not signed in,
SSH must answer, a download without credentials must be refused - and a file
published over the JSON API must be fetched by a downloader made over the
JSON API as it was published, whole and from a byte on, and its download be
recorded. **Nightly** runs every test of every library at the commits pinned
here - but for those that ask real name and time servers on the Internet -
starts the server the same way, says which packages have known
vulnerabilities, and builds and tests once more with every library at its
newest commit, to say what advancing them would cost.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.

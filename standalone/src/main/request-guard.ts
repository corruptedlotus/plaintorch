import { session } from "electron"

/**
 * Refuses every `file:` request a window makes that names a host. On Windows, Chromium opens `file://host/share/x`
 * as the UNC path `\\host\share\x`, and the SMB connection behind it hands that host the user's NTLM credentials — a
 * note whose icon is `//host/x.png` would do it without a click, since a protocol-relative URL on a page served from
 * a file resolves to exactly that. The pages' own policies already refuse such images; this holds for anything they
 * miss. The shell's windows load only local files, so nothing legitimate is lost, and the main process's own reads
 * (the media scheme serving a vault on a network share) come from no window and pass. Must run once the app is ready.
 */
export function refuseRemoteFiles(): void {
	session.defaultSession.webRequest.onBeforeRequest((details, callback) => {
		callback({ cancel: details.webContentsId !== undefined && isRemoteFile(details.url) })
	})
}

function isRemoteFile(url: string): boolean {
	if (!/^file:/i.test(url)) {
		return false
	}

	try {
		// The URL parser folds `file://localhost/…` to an empty host; any host left is a remote one. A path that still
		// begins with two slashes once decoded (`file:////host/share`, `file:///%5C%5Chost`, `\\?\UNC\…`) is a UNC
		// path too, whatever the host says.
		const parsed = new URL(url)
		return parsed.host !== "" || decodeURIComponent(parsed.pathname).replace(/\\/g, "/").startsWith("//")
	}
	catch {
		return true
	}
}

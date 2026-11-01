# Exporter video and installation page

The homepage contains the existing tutorial card and chapter buttons, followed
by installation for ALCOM/VCC and support. The duplicate transfer three-step
section and the separate written setup guide have been retired.

`guide/index.html` is only a compatibility redirect to the homepage video card
(`#tutorial`); it has no guide content and is excluded from the sitemap. Internal
navigation no longer links to it. Keep the fallback link for browsers without
JavaScript or automatic refresh.

The video, five subtitle tracks, reduced audio level and credits reference the
same published, owner-approved October 28 tutorial at `https://vrvlog.fun/guide/`.
The video loads only when requested; closing the dialog pauses it and restores
focus. Its thumbnail is the unmodified frame at exactly 0.0 seconds. The centered
SVG close icon has a 44px button. Never embed live QR codes or private names.

Installation uses two cards: add the exporter in the chosen manager, then install
it into the avatar project. The ALCOM/VCC selector is above the cards. Backup and
Unity-close instructions appear before the action, and manual URL instructions
remain in the fallback accordion. Switching, repository links and installation
image enlargement are preserved. The removed transfer step controls and their
styling are not used.
See `assets/guide/README.md` for the approved installation screenshots.

This is a Website-only publication: package code, manifests, versions, release
tags and archives are unchanged. Publish with the existing Build VPM Listing
workflow after merging the reviewed Website-only change into main, then back-merge
main into develop. The workflow generates the official-domain mirror and receipt.

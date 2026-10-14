# Tanuki artwork for Tanukarr

The project owner supplied [Tanuki_Logo.zip](Tanuki_Logo.zip). Its original artist
and license were not identified in this session; the contributor register
preserves that limitation and credits asset integration separately from artwork
authorship. The original archive and the PNG/SVG assets in `Logo/` remain the
source of the application and repository branding.

The repository README displays [Logo/800.png](../../Logo/800.png) directly. This
uses the supplied Tanuki artwork without altering it and avoids depending on a
large SVG containing an embedded raster image for GitHub rendering.

## GitHub repository social preview

[github-social-preview.png](github-social-preview.png) is a byte-identical copy
of the supplied `Logo/800.png`: 800 by 800 pixels, 687,718 bytes, SHA-256
`c99cd00bd721888697ab694eb8e52e26be698c470364bc117c75983a00bc0f64`.
No new artwork or raster transformation was introduced for this asset.

GitHub recommends a PNG, JPG or GIF under 1 MB and at least 640 by 320 pixels;
1280 by 640 is its recommended wide display size. The supplied PNG meets the
minimum dimensions and file-size guidance. The square logo may be cropped by
some link-preview platforms. GitHub supports transparent PNGs, although display
backgrounds differ between platforms.

For a repository administrator, GitHub's documented setup is:

1. Open [KvasExpert/Tanukarr settings](https://github.com/KvasExpert/Tanukarr/settings).
2. Under **Social preview**, choose **Edit**, then **Upload an image**.
3. Select `resources/branding/github-social-preview.png` from the checkout.

GitHub documents this repository-level setting in
[Customizing your repository's social media preview](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview).
Adding a file to Git or changing the README does not automatically change that
setting. This is repository branding; it does not change the publishing
account's profile avatar. The release verification report records whether the
GitHub setting was actually applied.

Application favicon, installer and platform assets are reproduced by
[docker/branding/update-assets.ps1](../../docker/branding/update-assets.ps1).
Original Sonarr notices remain in [COPYRIGHT.md](../../COPYRIGHT.md) and
[LICENSE.md](../../LICENSE.md). See [CONTRIBUTORS.md](../../CONTRIBUTORS.md) for
the supplied artwork's provenance and the OpenAI Codex based on GPT-6 integration
credit.

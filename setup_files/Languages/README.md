# Language catalogs

`English.lang` is the canonical catalog and the application's embedded fallback.

The English catalogs originally present in `setup_files`, `bin`, and the supplied
`mcskin3d_1_6_0_603` build were byte-identical and contained 235 interface strings.
Additional strings cover the modernized update check, square color picker and
skin destination browser. The destination browser falls back to the embedded
English strings when an older or translated catalog does not define its keys.

Save catalogs as **UTF-16 little-endian with a BOM**, as required by
`LanguageLoader`. Retain the `MCSkin3D Language File` header and culture metadata.
Keep string keys, numbered format placeholders such as `{0}`, and literal `\r`
and `\n` escapes consistent with English. The historical supported-version
metadata is retained for compatibility with the source catalog; it is not the
application or .NET runtime version.

Historical catalog copies remain unchanged.

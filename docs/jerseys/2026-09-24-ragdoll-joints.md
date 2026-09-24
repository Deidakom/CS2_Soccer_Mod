# Jersey ragdoll joints (2026-09-24)

## Problem

With a jersey on, a dying player didn't collapse. The body stayed stiff or
slid instead of falling to the ground. Without a jersey (stock agent model)
the ragdoll worked.

## Cause

The four kit models (`kit_home`, `kit_away`, `kit_gkhome`, `kit_gkaway`
`.vmdl_c`) were compiled with a `PHYS` block of 2,702 bytes. It has the
15 ragdoll bodies but **no joints** (`m_joints` is empty). Without joints the
bodies don't hang together as a ragdoll.

The stock `tm_leet_variantb.vmdl_c` that the kits are built from has a
4,736-byte `PHYS` block with 14 joints between the same 15 bodies.

## Fix

Replace the kits' `PHYS` block with the stock one. Every other block (mesh,
materials, animation, `DATA`) stays byte-identical.
[`tools/resource-blocks`](../../tools/resource-blocks/Program.cs) does this:

```bash
cd tools/resource-blocks
dotnet run -- swap in/kit_home.vmdl_c in/tm_leet_variantb.vmdl_c out/kit_home.vmdl_c PHYS
dotnet run -- list out/kit_home.vmdl_c   # PHYS:4736, the rest unchanged
```

The tool rewrites the resource data area (16-byte aligned, as the compiler
writes it) and patches the file size and the block table's relative offsets
and sizes. After writing, it checks that the swapped block equals the donor
and that every other block is unchanged.

The four patched models were put into the Workshop package with
`replace-vpk-files.ps1` (the C# `VpkEntryReplacer`) together with the new menu
layout. `Source2Viewer --vpk_verify` passed. Published to item 3797479770 on
2026-09-24 (33,917,313 bytes, Steam `time_updated` 1790284573). The previous
package is kept on the VPS as `/root/3797479770_dir.vpk.published-91d3`.

## When the kits are recompiled

A fresh `resourcecompiler` build of the kit `.vmdl` writes the jointless
`PHYS` again unless the source model defines the ragdoll joints. Re-run the
swap on the new `.vmdl_c` files before packing, or add the joints to the
source `.vmdl` (`PhysicsJointList`, taken from the stock agent) so the
compiler writes them itself.

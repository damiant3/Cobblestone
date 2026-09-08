# Reference: what is here, and what is withheld

`docs/Reference/` is published. Most of what it holds travels with the tree.

Five third-party documents do not, and this file is why you can still find
them. We read them to write the drivers; we do not redistribute them
(Damian, 2026-09-07). Each row below names the exact revision the depot
holds, so you can fetch the same document rather than a near neighbour.
Every one is free to obtain from its publisher, some behind a click-through
or a registration.

A withheld document is invisible from outside without a list like this one,
and a reader cannot tell a deliberate exclusion from a file somebody
dropped. That is the whole reason this file exists.

## Withheld, with their public sources

| Document | Revision the depot holds | Publisher | Where to get it |
|---|---|---|---|
| Universal Serial Bus Specification | Revision 2.0, 27 April 2000 | USB-IF | <https://www.usb.org/document-library/usb-20-specification> |
| Device Class Definition for Human Interface Devices (HID) | Version 1.11, 27 June 2001 | USB-IF | <https://www.usb.org/document-library/device-class-definition-hid-111> |
| eXtensible Host Controller Interface for Universal Serial Bus (xHCI) | Revision 1.2, May 2019 | Intel | <https://www.intel.com/content/www/us/en/content-details/625472/extensible-host-controller-interface-for-universal-serial-bus-xhci-requirements-specification.html> |
| Intel Ethernet Connection I219 Datasheet | Revision 2.02, May 2015 | Intel | <https://www.intel.com/content/www/us/en/content-details/612523/intel-ethernet-connection-i219-datasheet.html> |
| Intel 82583V GbE Controller Datasheet | Revision 2.6, June 2014 | Intel | <https://www.intel.com/content/www/us/en/products/sku/41676/intel-82583v-gigabit-ethernet-controller/specifications.html> |

Each is held in the depot as a `.pdf` and a `.txt` extract under the name in
the ignore rule at the bottom of `.gitignore`. The extracts exist because the
drivers are written against specific tables and a text search beats a page
number; they are the same content and are withheld on the same terms.

## What reads them

- `USB_2_0_Specification` and `HID_1_11_Specification`: the USB stack and the
  HID drivers, `codex/os/kernel` and the `GopUsb*` chapters in `apps/works`.
- `xHCI_Specification`: the xHCI host controller driver. Our own notes on it
  are `xHCI_ServiceModel_Notes.md`, which IS published, because we wrote it.
- `Intel_I219_Datasheet` and `Intel_82583V_Datasheet`: the e1000e driver and
  the NIC campaign in `docs/Designs/Active/OS/I219IsNotAnE1000.md`.

## The rest of this directory is published

Surveys, position papers, our own notes, and third-party documents whose
terms permit redistribution (an AMI firmware deep dive, an academic paper on
versioned e-graphs, and others) are all in the public tree and need no entry
here. Only the five above are withheld.

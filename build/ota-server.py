# ota-server.py -- an RFC 7252 + 7959 server that is not ours.
#
# Driven by build/ota-fetch-test.ps1. aiocoap performs the Block2
# segmentation itself, so the block walk the guest performs is answered by an
# independent implementation of RFC 7959 rather than by our own Coap chapter,
# which is the direction codex/test/apps/ota-lwm2m-loopback cannot point: its
# serve-block builds the responses our own codec expects.
#
# The payload is generated here, on the host, and the harness hashes it here
# too, so the digest Gate A re-derives inside the guest is compared against a
# number that was never computed by Codex.
#
# The first three bytes are the CDX magic because Gate A checks it. That is
# not decoration: serving the same bytes without it is the negative control,
# and it moves the guest's report from result=0 to result=6.

import asyncio
import sys

import aiocoap
import aiocoap.resource as resource


def firmware_body(size, magic):
    p = bytearray((i * 7 + i // 513) % 251 for i in range(size))
    if magic and size >= 3:
        p[0], p[1], p[2] = 67, 68, 88
    return bytes(p)


class Firmware(resource.Resource):
    def __init__(self, payload):
        super().__init__()
        self.payload = payload

    async def render_get(self, request):
        return aiocoap.Message(code=aiocoap.CONTENT, payload=self.payload)


async def main():
    size = int(sys.argv[1])
    port = int(sys.argv[2])
    magic = len(sys.argv) < 4 or sys.argv[3] != "nomagic"
    root = resource.Site()
    root.add_resource(("fw", "image.cdx"), Firmware(firmware_body(size, magic)))
    await aiocoap.Context.create_server_context(root, bind=("127.0.0.1", port))
    print("ota-server: %d bytes on %d magic=%s" % (size, port, magic), flush=True)
    await asyncio.get_running_loop().create_future()


asyncio.run(main())

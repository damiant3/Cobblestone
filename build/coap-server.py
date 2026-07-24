# coap-server.py -- an RFC 7252 server that is not ours.
#
# Driven by build/coap-interop-test.ps1. The payload is supplied on the
# command line by the harness so that the expected bytes are computed
# outside Codex entirely: the harness decides the string, tells this
# server to serve it, and separately computes the byte list the guest
# must report. Nothing in the loop can agree with itself.
#
# The resource name is likewise the harness's choice, so a client that
# hardcoded a path would fail rather than pass by coincidence.

import asyncio
import sys

import aiocoap
import aiocoap.resource as resource


class Fixed(resource.Resource):
    def __init__(self, payload):
        super().__init__()
        self.payload = payload

    async def render_get(self, request):
        return aiocoap.Message(code=aiocoap.CONTENT, payload=self.payload)


async def main():
    name = sys.argv[1]
    payload = sys.argv[2].encode("ascii")
    port = int(sys.argv[3])

    root = resource.Site()
    root.add_resource([name], Fixed(payload))
    # Anything not registered above is answered 4.04 by the Site itself,
    # which is the negative control the client is checked against.

    await aiocoap.Context.create_server_context(root, bind=("127.0.0.1", port))
    print("coap-server: aiocoap %s serving /%s on 127.0.0.1:%d"
          % (aiocoap.meta.version, name, port), flush=True)
    await asyncio.get_running_loop().create_future()


if __name__ == "__main__":
    asyncio.run(main())

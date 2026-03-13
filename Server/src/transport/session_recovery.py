"""Patch MCP SDK session manager to recover from stale session IDs.

When the server restarts, clients (e.g., Claude Code) continue sending the old
Mcp-Session-Id header. The SDK rejects these with HTTP 400. Since Claude Code
doesn't re-initialize (violating the MCP spec), tools break permanently.

This patch detects unknown session IDs and recreates a stateful session under
the same ID (pre-initialized to skip the handshake). The client never knows the
session was lost.

Workaround for:
  - https://github.com/anthropics/claude-code/issues/27142
  - https://github.com/modelcontextprotocol/python-sdk/issues/1676

Verified against mcp SDK v1.25.0.  Can be removed when the upstream client bug
is fixed.
"""

import logging

import anyio
from mcp.server.streamable_http import StreamableHTTPServerTransport
from mcp.server.streamable_http_manager import StreamableHTTPSessionManager
from starlette.requests import Request

logger = logging.getLogger(__name__)

MCP_SESSION_ID_HEADER = "mcp-session-id"

_original_handle = StreamableHTTPSessionManager._handle_stateful_request


async def _resilient_handle(self, scope, receive, send):
    request = Request(scope, receive)
    session_id = request.headers.get(MCP_SESSION_ID_HEADER)

    if session_id is not None and session_id not in self._server_instances:
        # Stale session — recreate under the same ID
        async with self._session_creation_lock:
            # Double-check: another request may have already recreated it
            if session_id in self._server_instances:
                transport = self._server_instances[session_id]
                await transport.handle_request(scope, receive, send)
                return

            logger.info(
                "Recovering stale MCP session %s via stateful re-creation",
                session_id[:8],
            )
            http_transport = StreamableHTTPServerTransport(
                mcp_session_id=session_id,
                is_json_response_enabled=self.json_response,
                event_store=self.event_store,
                security_settings=self.security_settings,
                retry_interval=self.retry_interval,
            )
            self._server_instances[session_id] = http_transport

            async def run_recovered(
                *, task_status=anyio.TASK_STATUS_IGNORED,
            ):
                async with http_transport.connect() as streams:
                    read_stream, write_stream = streams
                    task_status.started()
                    try:
                        await self.app.run(
                            read_stream,
                            write_stream,
                            self.app.create_initialization_options(),
                            stateless=True,  # pre-initialize only
                        )
                    except Exception:
                        logger.exception(
                            "Recovered session %s crashed", session_id[:8],
                        )
                    finally:
                        if (
                            http_transport.mcp_session_id
                            and http_transport.mcp_session_id
                            in self._server_instances
                            and not http_transport.is_terminated
                        ):
                            del self._server_instances[
                                http_transport.mcp_session_id
                            ]

            assert self._task_group is not None
            await self._task_group.start(run_recovered)
            await http_transport.handle_request(scope, receive, send)
        return

    await _original_handle(self, scope, receive, send)


def install():
    """Apply the session recovery patch. Call once at startup."""
    StreamableHTTPSessionManager._handle_stateful_request = _resilient_handle
    logger.info("MCP session recovery patch installed")

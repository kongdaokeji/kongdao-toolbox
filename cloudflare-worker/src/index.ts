export interface Env {
  GROUP_ROOM: DurableObjectNamespace;
}

interface PeerInfo {
  deviceId: string;
  deviceName: string;
  lanIp: string | null;
  ws: WebSocket;
  joinedAt: number;
}

function generateGroupCode(): string {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let code = "";
  for (let i = 0; i < 6; i++) {
    code += chars[Math.floor(Math.random() * chars.length)];
  }
  return code;
}

function jsonResponse(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type",
    },
  });
}

export class GroupRoom {
  private state: DurableObjectState;
  private peers: Map<string, PeerInfo> = new Map();
  private groupCode: string = "";
  private groupName: string = "";
  private creatorDeviceId: string = "";
  private createdAt: number = 0;
  private password: string = "";

  private loaded = false;

  constructor(state: DurableObjectState, _env: Env) {
    this.state = state;
  }

  private async ensureLoaded(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;
    const vals = await this.state.storage.list();
    if (vals.has("groupCode")) {
      this.groupCode = vals.get("groupCode") as string;
      this.groupName = vals.get("groupName") as string;
      this.creatorDeviceId = vals.get("creatorDeviceId") as string;
      this.createdAt = vals.get("createdAt") as number;
      this.password = vals.get("password") as string;
    }
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/init" && request.method === "POST") {
      return this.handleInit(request);
    }

    if (url.pathname === "/info") {
      return this.handleInfo();
    }

    if (url.pathname === "/ws") {
      return this.handleWebSocket(request);
    }

    if (url.pathname === "/message" && request.method === "POST") {
      return this.handleMessage(request);
    }

    return jsonResponse({ error: "Not found" }, 404);
  }

  private async handleInit(request: Request): Promise<Response> {
    await this.ensureLoaded();
    const body = (await request.json()) as {
      groupCode?: string;
      groupName?: string;
      password?: string;
      creatorDeviceId?: string;
    };

    if (this.groupCode && body.groupCode !== this.groupCode) {
      return jsonResponse({ error: "Group code mismatch" }, 400);
    }

    if (!this.groupCode) {
      this.groupCode = body.groupCode || generateGroupCode();
      this.groupName = body.groupName || `群组 ${this.groupCode}`;
      this.creatorDeviceId = body.creatorDeviceId || "";
      this.createdAt = Date.now();
      this.password = body.password || "";

      await this.state.storage.put({
        groupCode: this.groupCode,
        groupName: this.groupName,
        creatorDeviceId: this.creatorDeviceId,
        createdAt: this.createdAt,
        password: this.password,
      });
    }

    return jsonResponse({
      groupCode: this.groupCode,
      groupName: this.groupName,
      createdAt: this.createdAt,
    });
  }

  private async handleInfo(): Promise<Response> {
    await this.ensureLoaded();
    if (!this.groupCode) {
      return jsonResponse({ error: "Group not initialized" }, 404);
    }

    return jsonResponse({
      groupCode: this.groupCode,
      groupName: this.groupName,
      createdAt: this.createdAt,
      peerCount: this.peers.size,
      peers: Array.from(this.peers.values()).map((p) => ({
        deviceId: p.deviceId,
        deviceName: p.deviceName,
        lanIp: p.lanIp,
        joinedAt: p.joinedAt,
      })),
    });
  }

  private async handleWebSocket(request: Request): Promise<Response> {
    await this.ensureLoaded();
    const url = new URL(request.url);
    const deviceId = url.searchParams.get("deviceId") || "";
    const deviceName = url.searchParams.get("deviceName") || "";
    const lanIp = url.searchParams.get("lanIp") || null;
    const password = url.searchParams.get("password") || "";

    if (!deviceId) {
      return jsonResponse({ error: "deviceId required" }, 400);
    }

    if (this.password && password !== this.password) {
      return jsonResponse({ error: "Wrong password" }, 403);
    }

    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair) as [WebSocket, WebSocket];

    const peer: PeerInfo = {
      deviceId,
      deviceName,
      lanIp,
      ws: server,
      joinedAt: Date.now(),
    };

    this.state.acceptWebSocket(server);

    this.peers.set(deviceId, peer);

    server.addEventListener("message", (event) => {
      try {
        const msg = JSON.parse(event.data as string);
        msg.from = deviceId;

        if (msg.to) {
          const targetPeer = this.peers.get(msg.to as string);
          if (targetPeer) {
            this.sendTo(targetPeer.ws, msg);
          }
        }
      } catch {
        // ignore non-JSON messages
      }
    });

    this.broadcast(
      {
        type: "device-joined",
        device: {
          deviceId,
          deviceName,
          lanIp,
          joinedAt: peer.joinedAt,
        },
      },
      deviceId
    );

    const existingPeers = Array.from(this.peers.values())
      .filter((p) => p.deviceId !== deviceId)
      .map((p) => ({
        deviceId: p.deviceId,
        deviceName: p.deviceName,
        lanIp: p.lanIp,
        joinedAt: p.joinedAt,
      }));

    this.sendTo(server, {
      type: "joined",
      groupCode: this.groupCode,
      groupName: this.groupName,
      peers: existingPeers,
    });

    server.addEventListener("close", () => {
      this.peers.delete(deviceId);
      this.broadcast({ type: "device-left", deviceId }, deviceId);

      if (this.peers.size === 0) {
        this.state.storage.deleteAll();
      }
    });

    return new Response(null, {
      status: 101,
      webSocket: client,
    });
  }

  private async handleMessage(request: Request): Promise<Response> {
    await this.ensureLoaded();
    const msg = (await request.json()) as { from: string; type: string; to?: string; [key: string]: unknown };

    const fromPeer = this.peers.get(msg.from);
    if (!fromPeer) {
      return jsonResponse({ error: "Unknown device" }, 400);
    }

    switch (msg.type) {
      case "sdp-offer":
      case "sdp-answer":
      case "ice-candidate": {
        const targetId = msg.to as string;
        const targetPeer = this.peers.get(targetId);
        if (targetPeer) {
          this.sendTo(targetPeer.ws, { ...msg, from: fromPeer.deviceId });
        }
        break;
      }
      case "file-offer":
      case "file-accept":
      case "file-reject":
      case "file-cancel": {
        const targetId = msg.to as string;
        const targetPeer = this.peers.get(targetId);
        if (targetPeer) {
          this.sendTo(targetPeer.ws, { ...msg, from: fromPeer.deviceId });
        }
        break;
      }
      case "ping": {
        this.sendTo(fromPeer.ws, { type: "pong", timestamp: Date.now() });
        break;
      }
      default:
        break;
    }

    return jsonResponse({ ok: true });
  }

  private sendTo(ws: WebSocket, data: unknown) {
    try {
      ws.send(JSON.stringify(data));
    } catch {
      // connection closed
    }
  }

  private broadcast(data: unknown, excludeDeviceId?: string) {
    const msg = JSON.stringify(data);
    for (const [id, peer] of this.peers) {
      if (id !== excludeDeviceId) {
        try {
          peer.ws.send(msg);
        } catch {
          this.peers.delete(id);
        }
      }
    }
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type",
        },
      });
    }

    if (url.pathname === "/api/group" && request.method === "POST") {
      const body = (await request.json()) as {
        groupName?: string;
        password?: string;
        creatorDeviceId?: string;
      };

      const groupCode = generateGroupCode();
      const id = env.GROUP_ROOM.idFromName(groupCode);
      const stub = env.GROUP_ROOM.get(id);

      const initResp = await stub.fetch(
        new Request("https://do/init", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            groupCode,
            groupName: body.groupName,
            password: body.password,
            creatorDeviceId: body.creatorDeviceId,
          }),
        })
      );

      return initResp;
    }

    if (url.pathname.startsWith("/api/group/") && request.method === "GET") {
      const groupCode = url.pathname.split("/").pop()!.toUpperCase();
      const id = env.GROUP_ROOM.idFromName(groupCode);
      const stub = env.GROUP_ROOM.get(id);

      return stub.fetch(new Request("https://do/info"));
    }

    if (url.pathname.startsWith("/ws/group/")) {
      const groupCode = url.pathname.split("/").pop()!.toUpperCase();
      const id = env.GROUP_ROOM.idFromName(groupCode);
      const stub = env.GROUP_ROOM.get(id);

      await stub.fetch(
        new Request("https://do/init", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ groupCode }),
        })
      );

      const wsUrl = new URL(request.url);
      wsUrl.pathname = "/ws";

      const wsRequest = new Request(wsUrl.toString(), {
        headers: request.headers,
      });

      return stub.fetch(wsRequest);
    }

    return jsonResponse({ error: "Not found" }, 404);
  },
};

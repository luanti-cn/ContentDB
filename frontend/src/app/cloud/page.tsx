"use client";

import * as React from "react";
import { Cloud } from "lucide-react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { DevicesTab } from "@/components/cloud/devices-tab";
import { CharactersTab } from "@/components/cloud/characters-tab";
import { VaultTab } from "@/components/cloud/vault-tab";
import { SettingsTab } from "@/components/cloud/settings-tab";

export default function CloudPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex items-center gap-2">
        <Cloud className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">云同步</h1>
      </div>
      <p className="text-sm text-muted-foreground">
        绑定设备后,Luanti 客户端进服可自动取回账号密码;角色管理各服务器的登录身份。
      </p>

      <Tabs defaultValue="devices">
        <TabsList>
          <TabsTrigger value="devices">设备</TabsTrigger>
          <TabsTrigger value="characters">角色</TabsTrigger>
          <TabsTrigger value="vault">服务器凭证</TabsTrigger>
          <TabsTrigger value="settings">设置</TabsTrigger>
        </TabsList>
        <TabsContent value="devices">
          <DevicesTab />
        </TabsContent>
        <TabsContent value="characters">
          <CharactersTab />
        </TabsContent>
        <TabsContent value="vault">
          <VaultTab />
        </TabsContent>
        <TabsContent value="settings">
          <SettingsTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}

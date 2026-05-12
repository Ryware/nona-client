export type NonaContentType = "string" | "number" | "boolean" | "json";
export type NonaConfigScope = "client" | "server" | "all";
export type NonaApiKeyType = "server" | "client" | "both";
export type NonaUserRole = "viewer" | "editor";

export interface NonaClientOptions {
  baseUrl: string | URL;
  apiKey?: string;
  bearerToken?: string;
  fetch?: typeof fetch;
  defaultHeaders?: HeadersInit;
}

export interface NonaRequestOptions {
  signal?: AbortSignal;
}

export interface NonaConfigValue {
  value: string;
  contentType: string;
}

export interface NonaConfigEntry {
  project: string;
  environment: string;
  key: string;
  value: string;
  contentType: NonaContentType | string;
  scope: NonaConfigScope | string;
  createdAt: string;
  updatedAt: string;
}

export interface NonaProject {
  id: number;
  name: string;
  urlSlug?: string | null;
  serverApiKey?: string | null;
  clientApiKey?: string | null;
  environments: string[];
  createdAt: string;
  updatedAt: string;
}

export interface NonaEnvironment {
  name: string;
  project: string;
  createdAt: string;
  updatedAt: string;
}

export interface NonaLoginResponse {
  token: string;
  username: string;
  role: string;
  expiresAt: string;
}

export interface NonaRegisterResult {
  success: boolean;
  response: NonaLoginResponse | null;
  error: string | null;
}

export interface NonaProjectAccess {
  projectName: string;
  role: string;
}

export interface NonaUser {
  id: number;
  email: string;
  name: string;
  role: string;
  scope: string;
  isAdmin: boolean;
  projects: NonaProjectAccess[];
  createdAt: string;
  updatedAt: string;
  resetPasswordToken?: string | null;
}

export interface NonaAuditLog {
  id: number;
  actor: string;
  actorIsSystem: boolean;
  action: string;
  target: string;
  project?: string | null;
  environment?: string | null;
  createdAt: string;
}

export interface NonaDashboardCounts {
  users: number;
  projects: number;
  configEntries: number;
}

export interface NonaUpsertConfigEntryRequest {
  value: string;
  contentType?: NonaContentType | string;
  scope?: NonaConfigScope | string;
}

export interface NonaCreateProjectRequest {
  name: string;
}

export interface NonaCreateEnvironmentRequest {
  name: string;
}

export interface NonaLoginRequest {
  email: string;
  password: string;
}

export interface NonaRequestPasswordResetRequest {
  email: string;
}

export interface NonaRerollApiKeysRequest {
  keyType: NonaApiKeyType | string;
}

export interface NonaCreateUserRequest {
  name: string;
  email: string;
  role?: NonaUserRole | string;
  scope?: NonaConfigScope | string;
}

export interface NonaUpdateUserRequest {
  name: string;
  role?: NonaUserRole | string;
  scope?: NonaConfigScope | string;
}

export interface NonaProjectAccessRequest {
  role: NonaUserRole | string;
}

import { anonymousApiClient } from "./anonymousClient";
import {
  throwWithStatus,
  unwrapData,
} from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

export type InvitationAcceptContext =
  components["schemas"]["InvitationAcceptContext"];

export async function getInvitationAcceptContext(
  token: string,
): Promise<InvitationAcceptContext> {
  const { data, error, response } = await anonymousApiClient.GET(
    "/api/v1/invitations/accept",
    {
      params: { query: { token } },
    },
  );
  if (error) throwWithStatus(error, response);
  return unwrapData(data);
}

export async function acceptInvitation(input: {
  token: string;
  password: string;
  displayName: string;
}): Promise<{ email: string }> {
  const { data, error, response } = await anonymousApiClient.POST(
    "/api/v1/invitations/accept",
    { body: input },
  );
  if (error) throwWithStatus(error, response);
  return unwrapData(data);
}

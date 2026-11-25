import type { APIRoute } from 'astro';

import {
  createServerApiClient,
  dataJsonResponse,
  isValidExternalId,
  isValidUuid,
  normalizeServerEnv,
  problemJsonResponse,
  resolveClientIp,
  resolveServerEnv,
  type ServerRuntimeEnv,
} from '../../lib/api-client';

/**
 * Pure, testable handler for `GET /api/price`.
 *
 * Both identifiers are validated before any backend call: `providerUid` must be
 * a UUID and `itemId` an opaque external id. This blocks path traversal and
 * wholesale-provider name probes at the BFF boundary.
 */
export async function handlePriceRequest(
  request: Request,
  env: ServerRuntimeEnv = {},
  fetchFn?: typeof fetch,
): Promise<Response> {
  const params = new URL(request.url).searchParams;
  const providerUid = (params.get('providerUid') ?? '').trim();
  const itemId = (params.get('itemId') ?? '').trim();

  if (!isValidUuid(providerUid)) {
    return problemJsonResponse({
      status: 400,
      code: 'invalid_request',
      title: 'Invalid request',
      detail: 'providerUid must be a valid UUID.',
    });
  }

  if (!isValidExternalId(itemId)) {
    return problemJsonResponse({
      status: 400,
      code: 'invalid_request',
      title: 'Invalid request',
      detail: 'itemId must be 1-64 letters, digits, underscores or hyphens.',
    });
  }

  const client = createServerApiClient(normalizeServerEnv(env), {
    clientIp: resolveClientIp(request),
    fetchFn,
  });

  const result = await client.getItemPrice(providerUid, itemId);

  if (!result.ok) {
    return problemJsonResponse(result.error);
  }

  return dataJsonResponse(result.data, result.status, 'no-store');
}

export const GET: APIRoute = ({ request, locals }) => handlePriceRequest(request, resolveServerEnv(locals));

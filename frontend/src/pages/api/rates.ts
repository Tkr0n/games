import type { APIRoute } from 'astro';

import {
  createServerApiClient,
  dataJsonResponse,
  normalizeServerEnv,
  problemJsonResponse,
  resolveClientIp,
  resolveServerEnv,
  type ServerRuntimeEnv,
} from '../../lib/api-client';

const CACHE_CONTROL = 'public, max-age=30';

/**
 * Pure, testable handler for `GET /api/rates`.
 *
 * The query string is ignored on purpose: only the backend `/api/v1/rates`
 * response is proxied, with the BFF credentials and visitor IP attached.
 */
export async function handleRatesRequest(
  request: Request,
  env: ServerRuntimeEnv = {},
  fetchFn?: typeof fetch,
): Promise<Response> {
  const client = createServerApiClient(normalizeServerEnv(env), {
    clientIp: resolveClientIp(request),
    fetchFn,
  });

  const result = await client.getRates();

  if (!result.ok) {
    return problemJsonResponse(result.error);
  }

  return dataJsonResponse(result.data, result.status, CACHE_CONTROL);
}

export const GET: APIRoute = ({ request, locals }) => handleRatesRequest(request, resolveServerEnv(locals));

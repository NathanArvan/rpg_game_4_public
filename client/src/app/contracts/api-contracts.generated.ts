/* eslint-disable */
/* tslint:disable */
// @ts-nocheck
/*
 * ---------------------------------------------------------------
 * ## THIS FILE WAS GENERATED VIA SWAGGER-TYPESCRIPT-API        ##
 * ##                                                           ##
 * ## AUTHOR: acacode                                           ##
 * ## SOURCE: https://github.com/acacode/swagger-typescript-api ##
 * ---------------------------------------------------------------
 */

export interface AuthResponse {
  token?: string | null;
}

export interface BaseStatsDto {
  /** @format int32 */
  power?: number;
  /** @format int32 */
  speed?: number;
  /** @format int32 */
  precision?: number;
  /** @format int32 */
  resilience?: number;
  /** @format int32 */
  focus?: number;
  /** @format int32 */
  influence?: number;
}

export interface CharacterResponse {
  /** @format uuid */
  id?: string;
  name?: string | null;
  race?: string | null;
  class?: string | null;
  /** @format int32 */
  level?: number;
  baseStats?: BaseStatsDto;
  /** @format int32 */
  maxHp?: number;
  /** @format int32 */
  currentHp?: number;
  /** @format int32 */
  movementRange?: number;
  /** @format int32 */
  visionRange?: number;
}

export interface CreateCampaignRequest {
  name?: string | null;
}

export interface CreateCampaignResponse {
  /** @format uuid */
  id?: string;
}

export interface CreateCharacterRequest {
  name?: string | null;
  race?: string | null;
  class?: string | null;
  /** @format int32 */
  level?: number;
  baseStats?: BaseStatsDto;
}

export interface RegisterRequest {
  username?: string | null;
  displayName?: string | null;
  password?: string | null;
}

export interface StartSessionResponse {
  joinCode?: string | null;
  /** @format uuid */
  gameSessionId?: string;
}

export interface TokenRequest {
  username?: string | null;
  password?: string | null;
}

export interface UpdateCharacterRequest {
  name?: string | null;
  race?: string | null;
  class?: string | null;
  /** @format int32 */
  level?: number;
  baseStats?: BaseStatsDto;
}

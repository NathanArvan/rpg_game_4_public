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
  str?: number;
  /** @format int32 */
  dex?: number;
  /** @format int32 */
  con?: number;
  /** @format int32 */
  int?: number;
  /** @format int32 */
  wis?: number;
  /** @format int32 */
  cha?: number;
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
  combatProfile?: string | null;
}

export interface CombatProfileAbilityDto {
  id?: string | null;
  name?: string | null;
  description?: string | null;
  /** @format int32 */
  apCost?: number;
  /** @format int32 */
  cooldown?: number;
}

export interface CombatProfileDto {
  id?: string | null;
  name?: string | null;
  description?: string | null;
  side?: string | null;
  /** @format int32 */
  maxHp?: number;
  /** @format int32 */
  movementRange?: number;
  /** @format int32 */
  speed?: number;
  /** @format int32 */
  meleeAccuracy?: number;
  /** @format int32 */
  rangedAccuracy?: number;
  /** @format int32 */
  dodge?: number;
  baseDamage?: string | null;
  /** @format int32 */
  maxAp?: number;
  abilities?: CombatProfileAbilityDto[] | null;
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
  /** @format int32 */
  maxHp?: number;
  /** @format int32 */
  currentHp?: number;
  combatProfile?: string | null;
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
  /** @format int32 */
  maxHp?: number;
  /** @format int32 */
  currentHp?: number;
  combatProfile?: string | null;
}

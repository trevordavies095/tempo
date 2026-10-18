'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Tabs } from '@/components/ui/Tabs';
import {
  createCoachEpisode,
  createCoachRule,
  listCoachEpisodes,
  listCoachRules,
  retireCoachEpisode,
  retireCoachRule,
  unretireCoachEpisode,
  unretireCoachRule,
  type CoachEpisodeCategory,
  type CoachRuleTargetLayer,
} from '@/lib/api';
import {
  categoryLabel,
  memorySourceLabel,
  targetLayerLabel,
} from '@/lib/coachMemoryDisplay';
import { formatCoachDate } from '@/lib/coachWeek';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const PRIVACY_NOTICE =
  'Sent to Anthropic with your weekly packet — avoid names and addresses.';

const EPISODE_CATEGORIES: CoachEpisodeCategory[] = [
  'race',
  'breakthrough',
  'issue',
  'equipment',
  'milestone',
  'protocol_change',
  'recap',
];

const RULE_LAYERS: CoachRuleTargetLayer[] = [
  'data_interpretation_notes',
  'key_learnings',
  'coaching_preferences',
];

const TAB_ITEMS = [
  { id: 'episodes' as const, label: 'Events' },
  { id: 'rules' as const, label: 'Preferences' },
];

function todayIso(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function MemoryPageContent() {
  const queryClient = useQueryClient();

  const [tab, setTab] = useState<'episodes' | 'rules'>('episodes');
  const [showRetiredEpisodes, setShowRetiredEpisodes] = useState(false);
  const [showRetiredRules, setShowRetiredRules] = useState(false);
  const [categoryFilter, setCategoryFilter] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  const [episodeDate, setEpisodeDate] = useState(todayIso());
  const [episodeCategory, setEpisodeCategory] = useState<CoachEpisodeCategory>('milestone');
  const [episodeSummary, setEpisodeSummary] = useState('');

  const [ruleLayer, setRuleLayer] = useState<CoachRuleTargetLayer>('key_learnings');
  const [ruleText, setRuleText] = useState('');
  const [ruleEvidence, setRuleEvidence] = useState('');

  const episodesQuery = useQuery({
    queryKey: ['coach-episodes', showRetiredEpisodes, categoryFilter],
    queryFn: () =>
      listCoachEpisodes({
        includeRetired: showRetiredEpisodes,
        category: categoryFilter || undefined,
      }),
  });

  const rulesQuery = useQuery({
    queryKey: ['coach-rules', showRetiredRules],
    queryFn: () => listCoachRules({ includeRetired: showRetiredRules }),
  });

  const createEpisodeMutation = useMutation({
    mutationFn: () =>
      createCoachEpisode({
        date: episodeDate,
        category: episodeCategory,
        summary: episodeSummary.trim(),
      }),
    onSuccess: async () => {
      setError(null);
      setEpisodeSummary('');
      await queryClient.invalidateQueries({ queryKey: ['coach-episodes'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const createRuleMutation = useMutation({
    mutationFn: () =>
      createCoachRule({
        targetLayer: ruleLayer,
        ruleText: ruleText.trim(),
        evidence: ruleEvidence
          .split('\n')
          .map((line) => line.trim())
          .filter(Boolean),
      }),
    onSuccess: async () => {
      setError(null);
      setRuleText('');
      setRuleEvidence('');
      await queryClient.invalidateQueries({ queryKey: ['coach-rules'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const episodeToggleMutation = useMutation({
    mutationFn: async ({ id, retire }: { id: string; retire: boolean }) =>
      retire ? retireCoachEpisode(id) : unretireCoachEpisode(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['coach-episodes'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const ruleToggleMutation = useMutation({
    mutationFn: async ({ id, retire }: { id: string; retire: boolean }) =>
      retire ? retireCoachRule(id) : unretireCoachRule(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['coach-rules'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const episodes = episodesQuery.data ?? [];
  const rules = useMemo(() => rulesQuery.data ?? [], [rulesQuery.data]);

  const rulesByLayer = useMemo(() => {
    const groups: Record<string, typeof rules> = {};
    for (const layer of RULE_LAYERS) {
      groups[layer] = [];
    }
    for (const rule of rules) {
      const key = RULE_LAYERS.includes(rule.targetLayer as CoachRuleTargetLayer)
        ? rule.targetLayer
        : rule.targetLayer;
      if (!groups[key]) {
        groups[key] = [];
      }
      groups[key].push(rule);
    }
    return groups;
  }, [rules]);

  const memoryEmpty =
    !episodesQuery.isLoading &&
    !rulesQuery.isLoading &&
    episodes.length === 0 &&
    rules.length === 0 &&
    !showRetiredEpisodes &&
    !showRetiredRules &&
    !categoryFilter;

  return (
    <PageShell
      density="control"
      title="What your coach knows about you"
      subtitle="Events and preferences your coach remembers"
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted hover:text-ink">
            ← Week
          </Link>
        </div>
      }
    >
      <div className="w-full max-w-2xl space-y-6">
        {memoryEmpty && (
          <Card>
            <EmptyState
              title="Tell your coach something"
              description="Add a past injury, a race result, or a preference you already know about yourself. Significant events and reusable preferences help future weeks — ordinary easy weeks do not need to live here."
            />
          </Card>
        )}

        {error && (
          <Card>
            <p className="text-sm text-danger">{error}</p>
          </Card>
        )}

        <Tabs
          items={TAB_ITEMS}
          value={tab}
          onChange={setTab}
          aria-label="What your coach knows"
        >
          {tab === 'episodes' && (
            <div className="space-y-6">
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Add event</h2>
                <p className="text-xs text-muted mb-4">{PRIVACY_NOTICE}</p>
                <div className="space-y-3">
                  <label className="block text-sm text-muted space-y-1">
                    <span>Date</span>
                    <input
                      type="date"
                      className={fieldClass}
                      value={episodeDate}
                      onChange={(e) => setEpisodeDate(e.target.value)}
                    />
                  </label>
                  <label className="block text-sm text-muted space-y-1">
                    <span>Category</span>
                    <select
                      className={fieldClass}
                      value={episodeCategory}
                      onChange={(e) =>
                        setEpisodeCategory(e.target.value as CoachEpisodeCategory)
                      }
                    >
                      {EPISODE_CATEGORIES.map((c) => (
                        <option key={c} value={c}>
                          {categoryLabel(c)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="block text-sm text-muted space-y-1">
                    <span>Summary (max 400)</span>
                    <textarea
                      className={fieldClass}
                      rows={3}
                      maxLength={400}
                      value={episodeSummary}
                      onChange={(e) => setEpisodeSummary(e.target.value)}
                      placeholder="What would still matter six months from now?"
                    />
                  </label>
                  <Button
                    type="button"
                    disabled={
                      createEpisodeMutation.isPending || episodeSummary.trim().length === 0
                    }
                    onClick={() => createEpisodeMutation.mutate()}
                  >
                    {createEpisodeMutation.isPending ? 'Saving...' : 'Add event'}
                  </Button>
                </div>
              </Card>

              <Card>
                <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
                  <h2 className="text-lg font-semibold text-ink">Events</h2>
                  <div className="flex flex-wrap items-center gap-3 text-sm">
                    <label className="flex items-center gap-2 text-muted">
                      <span>Category</span>
                      <select
                        className="rounded-tempo border border-border bg-canvas px-2 py-1 text-ink"
                        value={categoryFilter}
                        onChange={(e) => setCategoryFilter(e.target.value)}
                      >
                        <option value="">All</option>
                        {EPISODE_CATEGORIES.map((c) => (
                          <option key={c} value={c}>
                            {categoryLabel(c)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className="flex items-center gap-2 text-muted">
                      <input
                        type="checkbox"
                        checked={showRetiredEpisodes}
                        onChange={(e) => setShowRetiredEpisodes(e.target.checked)}
                      />
                      Show removed
                    </label>
                  </div>
                </div>

                {episodesQuery.isLoading ? (
                  <p className="text-sm text-muted">Loading...</p>
                ) : episodes.length === 0 ? (
                  <p className="text-sm text-muted">No events yet.</p>
                ) : (
                  <ul className="space-y-3">
                    {episodes.map((ep) => (
                      <li
                        key={ep.id}
                        className={`rounded-tempo border border-border p-3 ${
                          ep.retired ? 'opacity-60' : ''
                        }`}
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div className="min-w-0 space-y-1">
                            <div className="flex flex-wrap items-center gap-2 text-xs text-muted uppercase tracking-wide">
                              <span className="rounded-tempo border border-border px-2 py-0.5">
                                {categoryLabel(ep.category)}
                              </span>
                              <span>{formatCoachDate(ep.date)}</span>
                              <span>{memorySourceLabel(ep.source)}</span>
                              {ep.retired && <span>removed</span>}
                            </div>
                            <p className="text-sm text-ink">{ep.summary}</p>
                          </div>
                          <Button
                            type="button"
                            variant="secondary"
                            disabled={episodeToggleMutation.isPending}
                            onClick={() =>
                              episodeToggleMutation.mutate({
                                id: ep.id,
                                retire: !ep.retired,
                              })
                            }
                          >
                            {ep.retired ? 'Restore' : 'Remove'}
                          </Button>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            </div>
          )}

          {tab === 'rules' && (
            <div className="space-y-6">
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Add preference</h2>
                <p className="text-xs text-muted mb-4">{PRIVACY_NOTICE}</p>
                <div className="space-y-3">
                  <label className="block text-sm text-muted space-y-1">
                    <span>Category</span>
                    <select
                      className={fieldClass}
                      value={ruleLayer}
                      onChange={(e) =>
                        setRuleLayer(e.target.value as CoachRuleTargetLayer)
                      }
                    >
                      {RULE_LAYERS.map((layer) => (
                        <option key={layer} value={layer}>
                          {targetLayerLabel(layer)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="block text-sm text-muted space-y-1">
                    <span>Rule text</span>
                    <textarea
                      className={fieldClass}
                      rows={3}
                      value={ruleText}
                      onChange={(e) => setRuleText(e.target.value)}
                      placeholder="Reusable across future weeks — not a one-off fact"
                    />
                  </label>
                  <label className="block text-sm text-muted space-y-1">
                    <span>Evidence (optional, one line each)</span>
                    <textarea
                      className={fieldClass}
                      rows={3}
                      value={ruleEvidence}
                      onChange={(e) => setRuleEvidence(e.target.value)}
                    />
                  </label>
                  <Button
                    type="button"
                    disabled={createRuleMutation.isPending || ruleText.trim().length === 0}
                    onClick={() => createRuleMutation.mutate()}
                  >
                    {createRuleMutation.isPending ? 'Saving...' : 'Add preference'}
                  </Button>
                </div>
              </Card>

              <Card>
                <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
                  <h2 className="text-lg font-semibold text-ink">Preferences</h2>
                  <label className="flex items-center gap-2 text-sm text-muted">
                    <input
                      type="checkbox"
                      checked={showRetiredRules}
                      onChange={(e) => setShowRetiredRules(e.target.checked)}
                    />
                    Show removed
                  </label>
                </div>

                {rulesQuery.isLoading ? (
                  <p className="text-sm text-muted">Loading...</p>
                ) : rules.length === 0 ? (
                  <p className="text-sm text-muted">No preferences yet.</p>
                ) : (
                  <div className="space-y-6">
                    {Object.entries(rulesByLayer).map(([layer, layerRules]) => {
                      if (layerRules.length === 0) {
                        return null;
                      }
                      return (
                        <section key={layer}>
                          <h3 className="text-sm font-semibold text-ink mb-2">
                            {targetLayerLabel(layer)}
                          </h3>
                          <ul className="space-y-3">
                            {layerRules.map((rule) => {
                              const retired = rule.status === 'retired';
                              return (
                                <li
                                  key={rule.id}
                                  className={`rounded-tempo border border-border p-3 ${
                                    retired ? 'opacity-60' : ''
                                  }`}
                                >
                                  <div className="flex flex-wrap items-start justify-between gap-2">
                                    <div className="min-w-0 space-y-1">
                                      <div className="flex flex-wrap items-center gap-2 text-xs text-muted uppercase tracking-wide">
                                        <span>{memorySourceLabel(rule.source)}</span>
                                        {retired && <span>removed</span>}
                                      </div>
                                      <p className="text-sm text-ink">{rule.ruleText}</p>
                                      {(rule.evidence?.length ?? 0) > 0 && (
                                        <details className="text-xs text-muted">
                                          <summary className="cursor-pointer">Evidence</summary>
                                          <ul className="mt-1 list-disc pl-4 space-y-0.5">
                                            {rule.evidence.map((item, i) => (
                                              <li key={`${i}-${item}`}>{item}</li>
                                            ))}
                                          </ul>
                                        </details>
                                      )}
                                    </div>
                                    <Button
                                      type="button"
                                      variant="secondary"
                                      disabled={ruleToggleMutation.isPending}
                                      onClick={() =>
                                        ruleToggleMutation.mutate({
                                          id: rule.id,
                                          retire: !retired,
                                        })
                                      }
                                    >
                                      {retired ? 'Restore' : 'Remove'}
                                    </Button>
                                  </div>
                                </li>
                              );
                            })}
                          </ul>
                        </section>
                      );
                    })}
                  </div>
                )}
              </Card>
            </div>
          )}
        </Tabs>
      </div>
    </PageShell>
  );
}

export default function CoachMemoryPage() {
  return <MemoryPageContent />;
}

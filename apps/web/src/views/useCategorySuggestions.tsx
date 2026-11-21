import { useCallback, useEffect, useState } from 'react';
import { budgetsApi } from '../api/budgets';

/** Mesma ideia da chave do reporting: sem diferença de maiúsculas, acentos e espaços extras. */
const keyOf = (category: string) =>
  category.normalize('NFD').replace(/\p{M}/gu, '').trim().replace(/\s+/g, ' ').toLowerCase();

/**
 * BDG-7: categorias já usadas pelo usuário, para sugerir no campo de categoria (continua aceitando
 * texto livre). Sugestão é conveniência: se a busca falhar, o formulário segue sem ela.
 */
export function useCategorySuggestions() {
  const [categories, setCategories] = useState<string[]>([]);

  useEffect(() => {
    let cancelled = false;
    budgetsApi
      .categories()
      .then((result) => !cancelled && setCategories(result))
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, []);

  // A projeção só vê um lançamento novo segundos depois: a categoria recém-usada entra na hora.
  const remember = useCallback((category: string) => {
    const trimmed = category.trim();
    if (!trimmed) return;
    setCategories((current) =>
      current.some((c) => keyOf(c) === keyOf(trimmed))
        ? current
        : [...current, trimmed].sort((a, b) => a.localeCompare(b, 'pt-BR', { sensitivity: 'base' })),
    );
  }, []);

  return { categories, remember };
}

export function CategoryDatalist({ id, categories }: { id: string; categories: string[] }) {
  return (
    <datalist id={id} data-cy={id}>
      {categories.map((c) => (
        <option key={c} value={c} />
      ))}
    </datalist>
  );
}

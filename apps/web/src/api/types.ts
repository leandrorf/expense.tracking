export interface Account {
  id: string;
  name: string;
  scope: 'Personal' | 'Business';
  taxId: string | null;
  currency: string;
  isActive: boolean;
  createdAtUtc: string;
}

export interface CreditCard {
  id: string;
  accountId: string;
  label: string;
  brand: string;
  lastFourDigits: string;
  creditLimit: number;
  closingDay: number;
  dueDay: number;
  isActive: boolean;
  createdAtUtc: string;
}

export interface PaymentMethod {
  code: string;
  name: string;
  requiresCard: boolean;
}

export interface Transaction {
  id: string;
  accountId: string;
  type: 'Expense' | 'Income';
  paymentMethod: string;
  creditCardId: string | null;
  description: string;
  category: string;
  amount: number;
  currency: string;
  occurredOn: string;
  notes: string | null;
  createdAtUtc: string;
}

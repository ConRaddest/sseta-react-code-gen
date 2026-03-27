// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  BRoleSearchResponse,
} from "@/types/api.types"
import { FetchRequest, DEFAULT_PAGE_SIZE } from "@sseta/components"

interface BRoleContextType {
  // State
  items: BRoleSearchResponse[]
  totalRows: number
  lastSearchTerm: string
  lastFetchRequest: FetchRequest | null

  // Operations
  fetchItems: (fetchRequest: FetchRequest, shouldMerge?: boolean) => Promise<void>
  loadMoreItems: () => Promise<void>

  // Refresh
  refresh: () => Promise<void>
}

// Undefined default is intentional — enforced by the useBRole hook below.
const BRoleContext = createContext<BRoleContextType | undefined>(undefined)

export function BRoleProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({
    items: [],
    totalRows: 0,
    lastSearchTerm: "",
    lastFetchRequest: {
      pageNumber: 1,
      pageSize: DEFAULT_PAGE_SIZE,
      orderByList: [],
      filterByList: [],
    },
  })

  const fetchItems = async (body: FetchRequest, shouldMerge: boolean = false) => {
    try {
      const response = await Api.B.Role.search(body)
      const searchFilter = body.filterByList?.find((f) => f.operator === "search")
      const searchTerm = searchFilter ? (searchFilter.value as string).replace(/\*/g, "") : ""
      setState((prev: any) => ({
        ...prev,
        items: shouldMerge ? [...prev.items, ...(response.data.searchResults || [])] : response.data.searchResults || [],
        totalRows: response.data?.totalRows || 0,
        lastSearchTerm: searchTerm,
        lastFetchRequest: body,
      }))
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const refresh = async () => { if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest) }
  const loadMoreItems = async () => {
    if (state.lastFetchRequest) {
      await fetchItems({ ...state.lastFetchRequest, pageNumber: state.lastFetchRequest.pageNumber + 1 }, true)
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <BRoleContext.Provider
      value={{
        ...state,
        fetchItems,
        loadMoreItems,
        refresh,
      }}
    >
      {children}
    </BRoleContext.Provider>
  )
}

// Throws if used outside of BRoleProvider to catch missing provider wrapping early.
export function useBRole() {
  const context = useContext(BRoleContext)
  if (context === undefined) {
    throw new Error("useBRole must be used within a BRoleProvider")
  }
  return context
}


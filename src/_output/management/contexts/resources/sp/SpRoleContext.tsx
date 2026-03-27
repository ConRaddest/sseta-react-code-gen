// !!---------------------------------------------------!!
// !!---------- AUTO-GENERATED: Do not edit! -----------!!
// !!---------------------------------------------------!!

"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  SpRoleSearchResponse,
} from "@/types/api.types"
import { FetchRequest, DEFAULT_PAGE_SIZE } from "@sseta/components"

interface SpRoleContextType {
  // State
  items: SpRoleSearchResponse[]
  totalRows: number
  lastSearchTerm: string
  lastFetchRequest: FetchRequest | null

  // Operations
  fetchItems: (fetchRequest: FetchRequest, shouldMerge?: boolean) => Promise<void>
  loadMoreItems: () => Promise<void>

  // Refresh
  refresh: () => Promise<void>
}

// Undefined default is intentional — enforced by the useSpRole hook below.
const SpRoleContext = createContext<SpRoleContextType | undefined>(undefined)

export function SpRoleProvider({ children }: { children: ReactNode }) {
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
      const response = await Api.SP.Role.search(body)
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
    <SpRoleContext.Provider
      value={{
        ...state,
        fetchItems,
        loadMoreItems,
        refresh,
      }}
    >
      {children}
    </SpRoleContext.Provider>
  )
}

// Throws if used outside of SpRoleProvider to catch missing provider wrapping early.
export function useSpRole() {
  const context = useContext(SpRoleContext)
  if (context === undefined) {
    throw new Error("useSpRole must be used within a SpRoleProvider")
  }
  return context
}

